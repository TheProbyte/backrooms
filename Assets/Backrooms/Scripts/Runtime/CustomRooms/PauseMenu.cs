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

        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            // A new scene starts running; never carry a paused clock or a stuck input block into it.
            _open = false;
            Time.timeScale = 1f;
            CamcorderPlayerController.InputBlocked = false;
            _status = "";
            if (s.name == SceneIds.Build) CustomRoomTest.End();      // back from a test: the room stays open in the editor
            CustomRoomTest.OnLoad();
        }

        bool InBuilder => SceneManager.GetActiveScene().name == SceneIds.Build;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame && ToggledFrame != Time.frameCount)
            {
                if (_open) Close();
                else if (!InBuilder && !_f5WasOpen) Open();       // the builder opens the menu itself when nothing else wants Escape
            }
        }

        void LateUpdate()
        {
            // The F5 panel closes itself on Escape; remember whether it was open when the frame began.
            var menu = FindFirstObjectByType<CamcorderVisualMenu>();
            _f5WasOpen = menu && menu.IsOpen;
        }

        public void Open()
        {
            if (_open) return;
            _open = true; ToggledFrame = Time.frameCount;
            _prevTimeScale = Time.timeScale;
            _prevLock = Cursor.lockState; _prevCursorVisible = Cursor.visible;
            _prevBlocked = CamcorderPlayerController.InputBlocked;
            if (!InBuilder) Time.timeScale = 0f;
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
            SceneManager.LoadScene(scene);
        }

        void OnGUI()
        {
            if (!_open) return;
            ImUi.Begin();
            string scene = SceneManager.GetActiveScene().name;
            ImUi.Fill(new Rect(0, 0, ImUi.Width, ImUi.Height), new Color(0, 0, 0, 0.62f));
            float w = 420, cx = ImUi.Width * 0.5f - w * 0.5f;
            bool testing = BuildSession.Testing && scene == SceneIds.World;
            int rows = 4 + (testing ? 1 : 0) + (scene != SceneIds.Reference ? 1 : 0);
            float h = 24 + 40 + 10 + rows * 56 + 24;
            var box = new Rect(cx, ImUi.Height * 0.5f - h * 0.5f, w, h);
            ImUi.PanelBox(box);
            var col = new ImUi.Column(box.x + 30, box.y + 24, w - 60, 48, 8);
            GUI.Label(col.Row(40), "MENU", ImUi.Title);
            col.Space(10);

            string resume = InBuilder ? "BACK TO BUILDING" : "RESUME";
            if (ImUi.Button(col.Row(), resume, true)) Close();
            if (testing)
            {
                if (ImUi.Button(col.Row(), "BACK TO THE BUILDER")) { Close(); Load(SceneIds.Build); }
            }
            if (ImUi.Button(col.Row(), scene == SceneIds.World ? "PLAY (RESTART THE WORLD)" : "PLAY — PROCEDURAL WORLD"))
            {
                Close(); CustomRoomTest.End();
                Load(SceneIds.World);
            }
            if (scene != SceneIds.Reference && ImUi.Button(col.Row(), "REFERENCE ROOM")) { Close(); CustomRoomTest.End(); Load(SceneIds.Reference); }
            if (ImUi.Button(col.Row(), InBuilder ? "ROOM LIBRARY" : "BUILD A SECTION"))
            {
                Close(); CustomRoomTest.End();
                if (InBuilder) BuilderApp.Current?.ShowLibrary(); else Load(SceneIds.Build);
            }
            if (ImUi.Button(col.Row(), "QUIT"))
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
