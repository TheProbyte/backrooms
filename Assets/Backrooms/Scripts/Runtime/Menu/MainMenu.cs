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

        enum Page { Top, Saves, Settings, Multi, Host, Join }
        Page _page;
        UIDocument _doc;
        VisualElement _root, _panel, _list, _header, _title;
        Label _pageLabel, _heading;
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
            if (_osd) { _osd.standby = true; _osd.SetStart(new DateTime(1996, 3, 11, 9, 0, 0)); }

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
            // The name and the menu sit inside the picture, clear of the black bar (they sat across its edge).
            if (_panel != null)
            {
                var inPicture = new Length((Backrooms.UI.UiKit.BarShare + 0.035f) * 100f, LengthUnit.Percent);
                _panel.style.left = inPicture;
                if (_title != null)
                {
                    _title.style.left = inPicture;
                    // Only on the first page, and not behind settings, YOUR LOOK or the camcorder's menu.
                    bool over = SettingsMenu.IsOpen || LookMenu.IsOpen || _vmShown || _page != Page.Top || _leaving;
                    _title.style.display = over ? DisplayStyle.None : DisplayStyle.Flex;
                }
            }
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

            if (SettingsMenu.IsOpen || SettingsMenu.ClosedFrame == Time.frameCount) return;     // the settings page has the keys
            if (LookMenu.IsOpen || LookMenu.ClosedFrame == Time.frameCount) return;             // so has YOUR LOOK
            var kb = Keyboard.current;
            UpdateNetStatus();
            if (kb == null || _leaving || _items.Count == 0) return;
            // Typing in a field: the keys are the field's (Enter or Escape ends it; Enter on the address joins).
            var focused = _root != null && _root.focusController != null ? _root.focusController.focusedElement as VisualElement : null;
            if (focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null))
            {
                bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                if (enter || kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) { focused.Blur(); if (enter && _page == Page.Join) JoinNow(); }
                return;
            }
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame, right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            if ((left || right) && _steps.TryGetValue(_sel, out var step)) { step(right ? 1 : -1); Beep(0); return; }
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Select((_sel + _items.Count - 1) % _items.Count, true);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Select((_sel + 1) % _items.Count, true);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Activate(_sel);
            else if ((kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) && _page != Page.Top)
            {
                Beep(2);
                if (_page == Page.Join && Net.NetSession.Active) Net.NetSession.Stop(null, false);       // (stops joining)
                Show(_page == Page.Host || _page == Page.Join ? Page.Multi : Page.Top);
            }
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
            _doc.panelSettings = Backrooms.UI.UiKit.Panel;
            _doc.sortingOrder = 10;
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/MainMenu");
            if (sheet) _root.styleSheets.Add(sheet);
            _root.AddToClassList("mm-root");
            _root.pickingMode = PickingMode.Ignore;
            ApplyMenuFont(_root);

            // The title screen (2026-11-10, Docs/UiPlan.md U8): the game's name in the company's colours over the picture,
            // the stripe under it. (OVERFLOW stands in until the owner names the game: the studio name is taken.)
            _title = new VisualElement(); _title.AddToClassList("tt-block"); _title.pickingMode = PickingMode.Ignore;
            _title.Add(Text(GameName, "tt-name"));
            var tstripe = new VisualElement(); tstripe.AddToClassList("tt-stripe"); tstripe.pickingMode = PickingMode.Ignore;
            foreach (var c in new[] { "mm-s1", "mm-s2", "mm-s3" }) { var b = new VisualElement(); b.AddToClassList(c); b.pickingMode = PickingMode.Ignore; tstripe.Add(b); }
            _title.Add(tstripe);
            _title.Add(Text("STORAGE & RETURNS  ·  SINCE 1961", "tt-tag"));
            _root.Add(_title);
            _root.Add(Text("BUILD " + Application.version, "tt-build"));

            _panel = new VisualElement();
            _panel.AddToClassList("mm-panel");
            _root.Add(_panel);
            _header = new VisualElement(); _header.AddToClassList("mm-header");
            _heading = Text("", "mm-title"); _header.Add(_heading);
            _pageLabel = Text("", "mm-page"); _header.Add(_pageLabel);
            _panel.Add(_header);
            var rule = new VisualElement(); rule.AddToClassList("mm-rule"); _panel.Add(rule);
            AddStripe(_panel);
            _list = new VisualElement(); _list.AddToClassList("mm-list"); _panel.Add(_list);
            _panel.Add(Text("▲▼ SELECT    ENTER OK    ESC BACK", "mm-hint"));
        }

        void Show(Page page)
        {
            _page = page;
            Disarm();
            ApplyLook(_root);
            _list.Clear(); _items.Clear(); _steps.Clear();
            _panel.EnableInClassList("mm-wide", page != Page.Top);
            _panel.EnableInClassList("tt-menu", page == Page.Top);
            if (_title != null) _title.style.display = page == Page.Top ? DisplayStyle.Flex : DisplayStyle.None;
            // Every menu was titled MENU: now the page says what it is (the first page needs no heading under the name).
            _header.style.display = page == Page.Top ? DisplayStyle.None : DisplayStyle.Flex;
            _heading.text = page == Page.Saves ? "WORLDS" : page == Page.Multi ? "PLAY WITH FRIENDS" : page == Page.Host ? "HOST A WORLD"
                          : page == Page.Join ? "JOIN A GAME" : page == Page.Settings ? "SETTINGS" : "";
            switch (page)
            {
                case Page.Top:
                    _pageLabel.text = "";
                    // Back from a co-op game that ended (the host left, the connection dropped): why, once.
                    if (!string.IsNullOrEmpty(Net.NetSession.LastError)) { _list.Add(Text(Net.NetSession.LastError, "mm-note")); Net.NetSession.LastError = null; }
                    Item("PLAY", () => { Beep(1); Show(Page.Saves); });
                    Item("PLAY WITH FRIENDS", () => { Beep(1); Show(Page.Multi); });
                    Item("YOUR LOOK", OpenLook);
                    Item("SETTINGS", OpenSettings);
                    Item("QUIT", Quit);
                    break;
                case Page.Saves:
                    _pageLabel.text = "";
                    for (int i = 0; i < SaveSlots.Count; i++) Slot(i);
                    Item("BACK", () => { Beep(2); Show(Page.Top); }, true);
                    break;
                case Page.Multi:
                    _pageLabel.text = "";
                    Field("NAME", Net.NetSession.PlayerName, 16, v => Net.NetSession.PlayerName = v);
                    Item("HOST A WORLD ►", () => { Beep(1); Show(Page.Host); });
                    Item("JOIN A GAME ►", () => { Beep(1); Show(Page.Join); });
                    _list.Add(Text("CO-OP FOR 2 TO 4 PLAYERS. ONE PLAYER HOSTS ONE OF THEIR WORLDS (IT IS SAVED IN THEIR SLOT, WITH WHAT EVERYONE DID); THE OTHERS JOIN WITH THE HOST'S ADDRESS. WORKS OVER HAMACHI, A LAN, OR ANY VPN (RADMIN, ZEROTIER, TAILSCALE).", "mm-desc"));
                    Item("BACK", () => { Beep(2); Show(Page.Top); }, true);
                    break;
                case Page.Host:
                {
                    _pageLabel.text = "";
                    var addrs = Net.NetTransport.LocalAddresses();
                    var said = new List<string>();
                    foreach (var (address, what) in addrs) if (said.Count < 3) said.Add(what + "  " + address);
                    _list.Add(Text(addrs.Count == 0 ? "NO NETWORK ADDRESS FOUND: CONNECT TO HAMACHI OR YOUR NETWORK FIRST." : "GIVE YOUR FRIENDS YOUR ADDRESS AND THE PORT:    " + string.Join("    ", said), "mm-desc"));
                    Field("PORT", Net.NetSession.Port.ToString(), 5, v => { if (int.TryParse(v, out int p)) Net.NetSession.Port = p; });
                    for (int i = 0; i < SaveSlots.Count; i++) HostSlot(i);
                    _netStatus = Text("", "mm-note"); _list.Add(_netStatus);
                    _list.Add(Text("THE FIRST TIME, WINDOWS ASKS IF THE GAME MAY USE THE NETWORK: ALLOW IT (PRIVATE AND PUBLIC).", "mm-desc"));
                    Item("BACK", () => { Beep(2); Show(Page.Multi); }, true);
                    break;
                }
                case Page.Join:
                    _pageLabel.text = "";
                    _addrField = Field("ADDRESS", Net.NetSession.LastAddress, 64, v => Net.NetSession.LastAddress = v.Trim());
                    Field("PORT", Net.NetSession.Port.ToString(), 5, v => { if (int.TryParse(v, out int p)) Net.NetSession.Port = p; });
                    Item("JOIN ►", JoinNow);
                    // Recent hosts (2026-11-10, U11): the last three joined, one press each.
                    foreach (var recent in RecentHosts())
                    {
                        string r = recent;
                        Item("JOIN " + r, () => { if (_addrField != null) _addrField.value = r; JoinNow(); }, true);
                    }
                    _netStatus = Text("", "mm-note"); _list.Add(_netStatus);
                    _list.Add(Text("THE HOST'S HAMACHI ADDRESS (25.X.X.X), THEIR LAN ADDRESS (192.168.X.X) OR ANY VPN'S, AND THEIR PORT (7777 UNLESS THEY CHANGED IT). CTRL+V PASTES.", "mm-desc"));
                    Item("BACK", () => { if (Net.NetSession.Active) Net.NetSession.Stop(null, false); Beep(2); Show(Page.Multi); }, true);
                    break;
                case Page.Settings:
                    _pageLabel.text = "";
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

        /// <summary>YOUR LOOK (Menu/LookMenu.cs) in place of the menu.</summary>
        void OpenLook()
        {
            Beep(1);
            _panel.style.display = DisplayStyle.None;
            LookMenu.Open(() => { if (!_vmShown) _panel.style.display = DisplayStyle.Flex; Select(_sel, false); });
        }

        /// <summary>
        /// The company's look on a menu: Overflow's own paperwork, chocolate with the amber bar and the company stripe
        /// (MainMenu.uss). Since 2026-11-10 the only look, so this only makes sure the kit is on the tree.
        /// </summary>
        internal static void ApplyLook(VisualElement root) => ApplyMenuFont(root);

        /// <summary>The company stripe under a menu's title (orange, gold, avocado; shown in the Overflow look only).</summary>
        internal static void AddStripe(VisualElement panel)
        {
            var stripe = new VisualElement(); stripe.AddToClassList("mm-stripe"); stripe.pickingMode = PickingMode.Ignore;
            foreach (var c in new[] { "mm-s1", "mm-s2", "mm-s3" }) { var b = new VisualElement(); b.AddToClassList(c); b.pickingMode = PickingMode.Ignore; stripe.Add(b); }
            panel.Add(stripe);
        }

        /// <summary>The settings page (Menu/SettingsMenu.cs) in place of the menu; the F5 tabs from it as ADVANCED.</summary>
        void OpenSettings()
        {
            Beep(1);
            _panel.style.display = DisplayStyle.None;
            SettingsMenu.Open(() => { if (!_vmShown) _panel.style.display = DisplayStyle.Flex; Select(_sel, false); }, _vm ? (Action)OpenCamcorderMenu : null);
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
            // A card (2026-11-10, U11): the world's own date, money in hand and stage first; time played and when last.
            body.Add(Text($"WORLD {slot + 1}", "mm-slotname"));
            if (d == null) body.Add(Text("EMPTY  —  START A NEW WORLD", "mm-empty"));
            else
            {
                var facts = new VisualElement(); facts.AddToClassList("sl-facts");
                facts.Add(Text(WorldDay(d), "sl-day"));
                facts.Add(Text($"${d.pay} IN HAND", "sl-fact"));
                facts.Add(Text(StageOf(d), "sl-fact"));
                body.Add(facts);
                body.Add(Text($"{Tape(d.playSeconds)} PLAYED    LAST {Stamp(d.saved)}" + (DevTools.On ? $"    SEED {d.seed}" : ""), "mm-detail"));
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

        // ---------------- Multiplayer (co-op, 2026-11-04) ----------------

        Label _netStatus;
        TextField _addrField;

        /// <summary>A row with a text field (the name, an address, a port): Enter or a click goes into it.</summary>
        TextField Field(string label, string value, int max, Action<string> changed)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item"); row.AddToClassList("mm-fieldrow");
            row.Add(Text("►", "mm-cursor"));
            row.Add(Text(label, "mm-fieldlabel"));
            var tf = new TextField { value = value ?? "", maxLength = max };
            tf.AddToClassList("mm-field");
            tf.RegisterValueChangedCallback(e => changed(e.newValue));
            row.Add(tf);
            Add(row, () => tf.Focus());
            return tf;
        }

        void HostSlot(int slot)
        {
            var d = SaveSlots.Load(slot);
            var row = new VisualElement(); row.AddToClassList("mm-item"); row.AddToClassList("mm-slot");
            row.Add(Text("►", "mm-cursor"));
            var body = new VisualElement(); body.AddToClassList("mm-slotbody");
            body.Add(Text($"HOST WORLD {slot + 1}", "mm-slotname"));
            if (d == null) body.Add(Text("EMPTY  —  A NEW WORLD", "mm-empty"));
            else
            {
                string zone = string.IsNullOrEmpty(d.zone) ? "" : d.zone.ToUpperInvariant() + "    ";
                int guests = d.guests != null ? d.guests.Count : 0;
                body.Add(Text($"{zone}{Tape(d.playSeconds)} ON TAPE" + (guests > 0 ? $"    {guests} OTHER PLAYER{(guests == 1 ? "" : "S")} BEFORE" : ""), "mm-detail"));
            }
            row.Add(body);
            Add(row, () => HostNow(slot));
        }

        void HostNow(int slot)
        {
            if (!Net.NetSession.Host(slot, out var err)) { Beep(2); if (_netStatus != null) _netStatus.text = err; return; }
            Beep(1);
            _leaving = true;
            _panel.style.display = DisplayStyle.None;
        }

        void JoinNow()
        {
            if (Net.NetSession.Active) return;
            string addr = (_addrField != null ? _addrField.value : Net.NetSession.LastAddress ?? "").Trim();
            int port = Net.NetSession.Port;
            // "25.1.2.3:7777" in one go.
            int colon = addr.LastIndexOf(':');
            if (colon > 0 && int.TryParse(addr.Substring(colon + 1), out int p2)) { port = p2; addr = addr.Substring(0, colon).Trim(); }
            if (addr.Length == 0) { Beep(2); if (_netStatus != null) _netStatus.text = "TYPE THE HOST'S ADDRESS FIRST"; return; }
            Net.NetSession.LastAddress = addr; Net.NetSession.Port = port;
            if (!Net.NetSession.Join(addr, port, out var err)) { Beep(2); if (_netStatus != null) _netStatus.text = err; return; }
            RememberHost(addr + ":" + port);
            Beep(1);
        }

        void UpdateNetStatus()
        {
            if (_netStatus == null || (_page != Page.Join && _page != Page.Host)) return;
            if (Net.NetSession.Active) _netStatus.text = Net.NetSession.Status;
            else if (!string.IsNullOrEmpty(Net.NetSession.LastError))
            {
                string e = Net.NetSession.LastError;
                // What to try, in plain words (2026-11-10, U11).
                string tip = _page != Page.Join ? "" : e.Contains("TIMED OUT") || e.Contains("NOTHING HEARD") ? "  CHECK THE ADDRESS AND PORT, THAT THE HOST HAS THEIR WORLD OPEN, AND THAT YOU ARE BOTH ON THE SAME HAMACHI OR VPN NETWORK."
                           : e.Contains("REFUSED") ? "  THE HOST'S GAME IS NOT LISTENING ON THAT PORT: ASK THEM FOR IT." : "";
                _netStatus.text = (_page == Page.Join ? "COULD NOT JOIN: " : "") + e + tip;
            }
        }

        /// <summary>The game's name on the title screen: a stand-in until the owner names it (Docs/UiPlan.md U8).</summary>
        public const string GameName = "OVERFLOW";

        /// <summary>A world's date on the camcorder (business time as client days from its first morning).</summary>
        static string WorldDay(WorldSaveData d)
        {
            var day = new DateTime(1996, 3, 11).AddDays(Math.Floor(d.business * Contracts.ClientDaysPerSecond));
            return day.ToString("MMM dd yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
        }

        static string StageOf(WorldSaveData d)
        {
            var names = WorkOrders.StageNames;
            return names[Mathf.Clamp(d.woStage, 0, names.Length - 1)];
        }

        const string RecentKey = "Backrooms.RecentHosts";

        /// <summary>The last hosts joined, newest first (address:port), at most three.</summary>
        static List<string> RecentHosts()
        {
            var list = new List<string>();
            foreach (var s in PlayerPrefs.GetString(RecentKey, "").Split('|')) if (s.Length > 0 && list.Count < 3) list.Add(s);
            return list;
        }

        static void RememberHost(string hostPort)
        {
            var list = RecentHosts();
            list.Remove(hostPort); list.Insert(0, hostPort);
            if (list.Count > 3) list.RemoveRange(3, list.Count - 3);
            PlayerPrefs.SetString(RecentKey, string.Join("|", list)); PlayerPrefs.Save();
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

        /// <summary>The menus' typeface and colour names: the kit's paper face (UiKit, Courier Prime).</summary>
        internal static void ApplyMenuFont(VisualElement root) => Backrooms.UI.UiKit.Apply(root, Backrooms.UI.UiKit.Face.Paper);
    }
}
