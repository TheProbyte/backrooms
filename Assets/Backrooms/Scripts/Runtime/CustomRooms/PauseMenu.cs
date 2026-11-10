using Backrooms.Camcorder;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Backrooms.CustomRooms
{
    public static class SceneIds
    {
        public const string World = "Backrooms_Infinite";
        public const string Reference = "ReferenceMatch";
        public const string Build = "BuildMode";
    }

    /// <summary>What build mode hands to the rest of the game: the room being edited, and the "test in the world" request.</summary>
    public static class BuildSession
    {
        /// <summary>The blueprint being edited (kept across scene loads so a test in the world can return to it).</summary>
        public static RoomBlueprint Editing;
        public static int Variant;
        public static bool Dirty;
        /// <summary>Set while the player walks a room under test in the procedural world.</summary>
        public static bool Testing;
    }

    /// <summary>
    /// Escape opens a simple menu in every scene: resume, play the procedural world, build a section, quit. It lives across scene
    /// loads. (In the builder, the builder decides when Escape means "close this" and when it means "menu".)
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public static PauseMenu Instance { get; private set; }
        public static bool IsOpen => Instance && Instance._open;
        /// <summary>Frame in which the menu was last opened or closed (so one Escape press is never handled twice).</summary>
        public static int ToggledFrame { get; private set; } = -1;

        bool _open, _f5WasOpen;
        CamcorderVisualMenu _visualMenu;
        float _prevTimeScale = 1f;
        CursorLockMode _prevLock;
        bool _prevCursorVisible;
        bool _prevBlocked;
        string _status = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance) return;
            var go = new GameObject("PauseMenu");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PauseMenu>();
        }

        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; _visualMenu = FindAnyObjectByType<CamcorderVisualMenu>(); }
        void Start() { _visualMenu = FindAnyObjectByType<CamcorderVisualMenu>(); }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            // A new scene starts running; never carry a paused clock or a stuck input block into it.
            _open = false;
            Time.timeScale = 1f;
            CamcorderPlayerController.InputBlocked = false;
            _status = "";
            _visualMenu = FindAnyObjectByType<CamcorderVisualMenu>();
            if (s.name == SceneIds.Build) CustomRoomTest.End();      // back from a test: the room stays open in the editor
            CustomRoomTest.OnLoad();
        }

        bool InBuilder => SceneManager.GetActiveScene().name == SceneIds.Build;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (Menu.SettingsMenu.IsOpen || Menu.SettingsMenu.ClosedFrame == Time.frameCount) return;   // the settings page has Escape
            if (Menu.LookMenu.IsOpen || Menu.LookMenu.ClosedFrame == Time.frameCount) return;           // so has YOUR LOOK
            if (kb.escapeKey.wasPressedThisFrame && ToggledFrame != Time.frameCount)
            {
                if (_open) Close();
                else if (!InBuilder && !_f5WasOpen && !Interaction.ItemsMenu.WasOpen && !Atmosphere.EnemiesMenu.WasOpen && !Interaction.ItemTuningMenu.WasOpen && !Camcorder.CamcorderPlayerMenu.WasOpen && !Menu.MainMenu.IsShowing) Open();     // those close themselves on Escape       // the builder opens the menu itself when nothing else wants Escape
            }
        }

        void LateUpdate()
        {
            UpdateUi();
            // The F5 panel closes itself on Escape; remember whether it was open when the frame began.
            _f5WasOpen = _visualMenu && _visualMenu.IsOpen;
        }

        public void Open()
        {
            if (_open) return;
            _open = true; ToggledFrame = Time.frameCount;
            _prevTimeScale = Time.timeScale;
            _prevLock = Cursor.lockState; _prevCursorVisible = Cursor.visible;
            _prevBlocked = CamcorderPlayerController.InputBlocked;
            if (!InBuilder && !Net.NetSession.Active) Time.timeScale = 0f;      // (co-op: the world goes on for the others)
            CamcorderPlayerController.InputBlocked = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            _status = "";
            Fill();
            if (_root != null) _root.style.display = DisplayStyle.Flex;
            Backrooms.UI.UiKit.Enter(_panel);
            Backrooms.UI.UiSounds.Play(Backrooms.UI.UiSound.Select);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false; ToggledFrame = Time.frameCount;
            Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale;
            CamcorderPlayerController.InputBlocked = _prevBlocked;
            if (_prevLock == CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (_root != null) _root.style.display = DisplayStyle.None;
            Disarm();
            Backrooms.UI.UiSounds.Play(Backrooms.UI.UiSound.Back);
        }

        static void Load(string scene)
        {
            if (!Application.CanStreamedLevelBeLoaded(scene)) { Debug.LogWarning($"[Backrooms] Scene '{scene}' is not in the build settings."); return; }
            Saves.GameSession.Leave();          // the world being played is saved first
            SceneManager.LoadScene(scene);
        }

        // ---------------- The menu (2026-11-10, Docs/UiPlan.md U9) ----------------
        // It was an IMGUI stack of buttons in its own layout, titled MENU. Now it is built as the main menu is (UI Toolkit,
        // MainMenu.uss: the same panel, list, cursor and keys, the mouse as well), titled PAUSED. QUIT, and ending a
        // hosted game for everyone, ask once (a second press within a few seconds), as ERASE does on a world.

        UIDocument _doc;
        VisualElement _root, _dim, _panel, _list;
        Label _info, _statusLabel;
        readonly System.Collections.Generic.List<(VisualElement el, System.Action act)> _items = new System.Collections.Generic.List<(VisualElement, System.Action)>();
        int _sel;
        string _armed;
        float _armedUntil;
        Label _armedLabel;
        string _armedText;

        void BuildUi()
        {
            if (_doc) return;
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = Backrooms.UI.UiKit.Panel;
            _doc.sortingOrder = 15;
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/MainMenu");
            if (sheet) _root.styleSheets.Add(sheet);
            _root.AddToClassList("mm-root");
            _root.pickingMode = PickingMode.Ignore;
            Menu.MainMenu.ApplyMenuFont(_root);
            _dim = new VisualElement { pickingMode = PickingMode.Position };
            _dim.style.position = Position.Absolute; _dim.style.left = 0; _dim.style.top = 0; _dim.style.right = 0; _dim.style.bottom = 0;
            var g = Backrooms.UI.UiKit.Ground; _dim.style.backgroundColor = new Color(g.r, g.g, g.b, 0.6f);
            _root.Add(_dim);
            _panel = new VisualElement(); _panel.AddToClassList("mm-panel"); _panel.AddToClassList("ps-panel");
            _root.Add(_panel);
            var header = new VisualElement(); header.AddToClassList("mm-header");
            header.Add(Lb("PAUSED", "mm-title"));
            _panel.Add(header);
            var rule = new VisualElement(); rule.AddToClassList("mm-rule"); _panel.Add(rule);
            Menu.MainMenu.AddStripe(_panel);
            _info = Lb("", "mm-desc"); _panel.Add(_info);
            _list = new VisualElement(); _list.AddToClassList("mm-list"); _panel.Add(_list);
            _statusLabel = Lb("", "mm-note"); _panel.Add(_statusLabel);
            _panel.Add(Lb("▲▼ SELECT    ENTER OK    ESC RESUME", "mm-hint"));
            _root.style.display = DisplayStyle.None;
        }

        static Label Lb(string text, string cls) { var l = new Label(text); l.AddToClassList(cls); l.pickingMode = PickingMode.Ignore; return l; }

        /// <summary>The entries for where you are (the world, a co-op game, the builder, a test).</summary>
        void Fill()
        {
            BuildUi();
            _list.Clear(); _items.Clear(); _armed = null; _armedLabel = null;
            string scene = SceneManager.GetActiveScene().name;
            bool testing = BuildSession.Testing && scene == SceneIds.World && !CustomRoomTest.Reference;
            bool net = Net.NetSession.Active;
            // The reference room and build mode are the developer's (DevTools, 2026-11-10).
            bool offerReference = !(CustomRoomTest.Reference && scene == SceneIds.World) && !net && DevTools.On;
            bool offerBuild = !net && (DevTools.On || InBuilder);

            // Co-op: who is here, and (hosting) the addresses to give a friend, each with COPY.
            _info.style.display = net ? DisplayStyle.Flex : DisplayStyle.None;
            if (net)
            {
                _info.text = Net.NetSession.IsHost ? $"HOSTING · {Net.NetSession.Peers.Count} OF {Net.NetSession.MaxPlayers} PLAYERS · PORT {Net.NetSession.Port}"
                                                   : $"IN {Net.NetSession.NameOf(0)}'S WORLD · {Net.NetSession.Peers.Count} PLAYERS";
                if (Net.NetSession.IsHost)
                {
                    var addresses = Net.NetTransport.LocalAddresses();
                    if (addresses.Count == 0) _list.Add(Lb("NO NETWORK ADDRESS FOUND", "ps-addrtext"));
                    for (int i = 0; i < addresses.Count && i < 3; i++)
                    {
                        string addr = $"{addresses[i].address}:{Net.NetSession.Port}";
                        var row = new VisualElement(); row.AddToClassList("ps-addr");
                        row.Add(Lb($"{addresses[i].what}:  {addr}", "ps-addrtext"));
                        var copy = Lb("COPY", "ps-copy"); copy.pickingMode = PickingMode.Position;
                        copy.RegisterCallback<ClickEvent>(_ => { GUIUtility.systemCopyBuffer = addr; copy.text = "COPIED"; Camcorder.CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.copied", addr)); });
                        row.Add(copy);
                        _list.Add(row);
                    }
                }
            }

            Item(InBuilder ? "BACK TO BUILDING" : "RESUME", Close);
            // (2026-11-04) The player's settings page, over the paused game.
            Item("SETTINGS", () => { _root.style.display = DisplayStyle.None; Menu.SettingsMenu.Open(Reopen); });
            // (2026-11-06) Your face in the suit; others in co-op see a change at once.
            Item("YOUR LOOK", () => { _root.style.display = DisplayStyle.None; Menu.LookMenu.Open(Reopen); });
            if (testing) Item("BACK TO THE BUILDER", () => { Close(); Load(SceneIds.Build); });
            if (scene == SceneIds.World && Saves.GameSession.NetClient)
                Item("LEAVE THE GAME", () => { Close(); Net.NetSession.Stop(null); });
            else if (scene == SceneIds.World && Saves.GameSession.Slot >= 0)
            {
                if (net) Confirmed("SAVE AND END THE GAME", "END IT FOR EVERYONE? AGAIN", () => { Close(); Saves.GameSession.ToMenu(); });
                else Item("SAVE AND QUIT TO MENU", () => { Close(); Saves.GameSession.ToMenu(); });
            }
            else Item("MAIN MENU", () => { Close(); CustomRoomTest.End(); Load(SceneIds.World); });
            // The reference room is a room of the procedural world (ReferenceBlueprint), lit exactly like the game.
            if (offerReference) Item("REFERENCE ROOM", () => { Close(); CustomRoomTest.End(); CustomRoomTest.BeginReference(); Load(SceneIds.World); });
            if (offerBuild) Item(InBuilder ? "ROOM LIBRARY" : "BUILD A SECTION", () =>
            {
                Close(); CustomRoomTest.End();
                if (InBuilder) BuilderApp.Current?.ShowLibrary(); else Load(SceneIds.Build);
            });
            // The world is saved as the game quits (WorldSaver).
            Confirmed("QUIT", "QUIT THE GAME? AGAIN", () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            Select(0);
        }

        void Reopen()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; CamcorderPlayerController.InputBlocked = true;
            if (_open && _root != null) _root.style.display = DisplayStyle.Flex;
        }

        Label Item(string text, System.Action act)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item");
            row.Add(Lb("►", "mm-cursor"));
            var l = Lb(text, "mm-text"); row.Add(l);
            int index = _items.Count;
            _items.Add((row, act));
            row.RegisterCallback<PointerEnterEvent>(_ => { if (_sel != index) Select(index); });
            row.RegisterCallback<ClickEvent>(_ => Activate(index));
            _list.Add(row);
            return l;
        }

        /// <summary>An entry that asks once: the first press changes its words, a second within four seconds does it.</summary>
        void Confirmed(string text, string ask, System.Action act)
        {
            Label l = null;
            l = Item(text, () =>
            {
                if (_armed == text && Time.unscaledTime < _armedUntil) { act(); return; }
                Disarm();
                _armed = text; _armedUntil = Time.unscaledTime + 4f; _armedLabel = l; _armedText = text;
                l.text = ask;
            });
        }

        void Disarm()
        {
            if (_armedLabel != null) _armedLabel.text = _armedText;
            _armed = null; _armedLabel = null;
        }

        void Select(int i)
        {
            if (_items.Count == 0) return;
            if (i != _sel && _root != null && _root.style.display == DisplayStyle.Flex) Backrooms.UI.UiSounds.Play(Backrooms.UI.UiSound.Move);
            _sel = Mathf.Clamp(i, 0, _items.Count - 1);
            for (int k = 0; k < _items.Count; k++) _items[k].el.EnableInClassList("mm-sel", k == _sel);
        }

        void Activate(int i)
        {
            if (i < 0 || i >= _items.Count) return;
            Select(i);
            Backrooms.UI.UiSounds.Play(Backrooms.UI.UiSound.Select);
            _items[i].act?.Invoke();
        }

        /// <summary>While open: the keys, the panel inside the picture, and hidden while settings or YOUR LOOK is over it.</summary>
        void UpdateUi()
        {
            if (_root == null) return;
            bool show = _open && !Menu.SettingsMenu.IsOpen && !Menu.LookMenu.IsOpen;
            _root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _panel.style.left = new Length((Backrooms.UI.UiKit.BarShare + 0.035f) * 100f, LengthUnit.Percent);
            if (_armed != null && Time.unscaledTime > _armedUntil) Disarm();
            _statusLabel.text = _status;
            var kb = Keyboard.current;
            if (kb == null || ToggledFrame == Time.frameCount || _items.Count == 0) return;
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Select((_sel + _items.Count - 1) % _items.Count);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Select((_sel + 1) % _items.Count);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Activate(_sel);
        }
    }
}
