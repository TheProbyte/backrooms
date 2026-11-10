using Backrooms.Camcorder;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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
                else if (!InBuilder && !_f5WasOpen && !Interaction.ItemsMenu.WasOpen && !Atmosphere.EnemiesMenu.WasOpen && !Interaction.ItemTuningMenu.WasOpen && !Menu.MainMenu.IsShowing) Open();     // those close themselves on Escape       // the builder opens the menu itself when nothing else wants Escape
            }
        }

        void LateUpdate()
        {
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
        }

        public void Close()
        {
            if (!_open) return;
            _open = false; ToggledFrame = Time.frameCount;
            Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale;
            CamcorderPlayerController.InputBlocked = _prevBlocked;
            if (_prevLock == CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        static void Load(string scene)
        {
            if (!Application.CanStreamedLevelBeLoaded(scene)) { Debug.LogWarning($"[Backrooms] Scene '{scene}' is not in the build settings."); return; }
            Saves.GameSession.Leave();          // the world being played is saved first
            SceneManager.LoadScene(scene);
        }

        void OnGUI()
        {
            if (!_open || Menu.SettingsMenu.IsOpen || Menu.LookMenu.IsOpen) return;
            ImUi.Begin();
            string scene = SceneManager.GetActiveScene().name;
            ImUi.Fill(new Rect(0, 0, ImUi.Width, ImUi.Height), new Color(0, 0, 0, 0.62f));
            float w = 420, cx = ImUi.Width * 0.5f - w * 0.5f;
            bool testing = BuildSession.Testing && scene == SceneIds.World && !CustomRoomTest.Reference;
            bool net = Net.NetSession.Active;
            // The reference room and build mode are the developer's (DevTools, 2026-11-10).
            bool offerReference = !(CustomRoomTest.Reference && scene == SceneIds.World) && !net && DevTools.On;
            bool offerBuild = !net && (DevTools.On || InBuilder);
            int rows = 5 + (testing ? 1 : 0) + (offerReference ? 1 : 0) + (offerBuild ? 1 : 0);
            var addresses = net && Net.NetSession.IsHost ? Net.NetTransport.LocalAddresses() : null;
            float extra = net ? 34 + (addresses != null ? 30 * Mathf.Min(3, Mathf.Max(1, addresses.Count)) : 0) : 0;
            float h = 24 + 40 + 10 + rows * 56 + 24 + extra;
            var box = new Rect(cx, ImUi.Height * 0.5f - h * 0.5f, w, h);
            ImUi.FramedBox(box);
            var col = new ImUi.Column(box.x + 30, box.y + 24, w - 60, 48, 8);
            GUI.Label(col.Row(40), "MENU", ImUi.Title);
            ImUi.Stripe(new Rect(col.x, col.y - 4, col.w, 6));
            col.Space(10);
            if (net)
            {
                // Co-op: who is here, and (hosting) the addresses to give a friend.
                GUI.Label(col.Row(26), Net.NetSession.IsHost ? $"HOSTING · {Net.NetSession.Peers.Count} OF {Net.NetSession.MaxPlayers} PLAYERS · PORT {Net.NetSession.Port}" : $"IN {Net.NetSession.NameOf(0)}'S WORLD · {Net.NetSession.Peers.Count} PLAYERS", ImUi.Small);
                if (addresses != null)
                {
                    if (addresses.Count == 0) GUI.Label(col.Row(22), "NO NETWORK ADDRESS FOUND", ImUi.Small);
                    for (int i = 0; i < addresses.Count && i < 3; i++) GUI.Label(col.Row(22), $"{addresses[i].what}:  {addresses[i].address}:{Net.NetSession.Port}", ImUi.Small);
                }
            }

            string resume = InBuilder ? "BACK TO BUILDING" : "RESUME";
            if (ImUi.Button(col.Row(), resume, true)) Close();
            // (2026-11-04) The player's settings page, over the paused game.
            if (ImUi.Button(col.Row(), "SETTINGS")) Menu.SettingsMenu.Open(() => { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; CamcorderPlayerController.InputBlocked = true; });
            // (2026-11-06) Your face in the suit; others in co-op see a change at once.
            if (ImUi.Button(col.Row(), "YOUR LOOK")) Menu.LookMenu.Open(() => { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; CamcorderPlayerController.InputBlocked = true; });
            if (testing)
            {
                if (ImUi.Button(col.Row(), "BACK TO THE BUILDER")) { Close(); Load(SceneIds.Build); }
            }
            if (scene == SceneIds.World && Saves.GameSession.NetClient)
            {
                if (ImUi.Button(col.Row(), "LEAVE THE GAME")) { Close(); Net.NetSession.Stop(null); }
            }
            else if (scene == SceneIds.World && Saves.GameSession.Slot >= 0)
            {
                if (ImUi.Button(col.Row(), net ? "SAVE AND END THE GAME" : "SAVE AND QUIT TO MENU")) { Close(); Saves.GameSession.ToMenu(); }
            }
            else if (ImUi.Button(col.Row(), "MAIN MENU"))
            {
                Close(); CustomRoomTest.End();
                Load(SceneIds.World);
            }
            // The reference room is a room of the procedural world (ReferenceBlueprint), lit exactly like the game.
            if (offerReference && ImUi.Button(col.Row(), "REFERENCE ROOM")) { Close(); CustomRoomTest.End(); CustomRoomTest.BeginReference(); Load(SceneIds.World); }
            if (offerBuild && ImUi.Button(col.Row(), InBuilder ? "ROOM LIBRARY" : "BUILD A SECTION"))
            {
                Close(); CustomRoomTest.End();
                if (InBuilder) BuilderApp.Current?.ShowLibrary(); else Load(SceneIds.Build);
            }
            if (ImUi.Button(col.Row(), "QUIT"))      // the world is saved as the game quits (WorldSaver)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            if (!string.IsNullOrEmpty(_status)) GUI.Label(col.Row(30), _status, ImUi.Small);
            ImUi.End();
        }
    }
}
