using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Atmosphere
{
    /// <summary>
    /// Developer menu "Enemies" (K): test the enemies without waiting for them (owner, 2026-10-31). A panel down the
    /// right side, the level still in view: every enemy registered with <see cref="EnemyDebug"/> with its status and its
    /// buttons (bring it, push it to a stage, send it away), the tension director's phase with a few of its events, and
    /// the switches (the director hands off, can't be caught, freeze, show on screen). With "show on screen" its status
    /// stays in the corner and a marker shows where it really is (and where it shows) with the menu closed too.
    /// K or Escape closes it. Added by the tension director.
    /// </summary>
    public class EnemiesMenu : MonoBehaviour
    {
        public static EnemiesMenu Instance { get; private set; }
        public static bool IsOpen => Instance && Instance._open;
        /// <summary>Open when the frame began (the pause menu leaves Escape to us then).</summary>
        public static bool WasOpen { get; private set; }

        bool _open, _prevBlocked;
        CursorLockMode _prevLock;
        int _openedFrame = -1;
        Vector2 _scroll;
        float _contentH = 800f;
        readonly List<DevAction> _actions = new List<DevAction>();

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            // (2026-11-08 profile: the search for the player below cost 1.9 ms every frame. Only when it matters now.)
            if (!_open && (!kb.kKey.wasPressedThisFrame || !DevTools.On)) return;   // a developer's tool (DevTools)
            var ws = WorldStreamer.Current;
            bool can = ws && !ws.MenuMode && FindAnyObjectByType<CamcorderPlayerController>();
            if (_open && !can) { SetOpen(false); return; }
            bool otherMenu = PauseMenu.IsOpen || (CamcorderPlayerController.InputBlocked && !_open);
            if (kb.kKey.wasPressedThisFrame && can && !otherMenu && !TypingInField()) SetOpen(!_open);
            else if (_open && kb.escapeKey.wasPressedThisFrame && _openedFrame != Time.frameCount) SetOpen(false);
        }

        static bool TypingInField() => GUI.GetNameOfFocusedControl().StartsWith("tf_");

        void LateUpdate() { WasOpen = _open; }

        public void SetOpen(bool open)
        {
            if (open == _open) return;
            _open = open;
            if (open)
            {
                _openedFrame = Time.frameCount;
                _prevLock = Cursor.lockState;
                _prevBlocked = CamcorderPlayerController.InputBlocked;
                CamcorderPlayerController.InputBlocked = true;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            else
            {
                CamcorderPlayerController.InputBlocked = _prevBlocked;
                if (_prevLock == CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
        }

        // ---------------- UI ----------------

        void OnGUI()
        {
            if (!_open && !EnemyDebug.Overlay) return;
            var ws = WorldStreamer.Current;
            if (!ws || ws.MenuMode) return;
            ImUi.Begin();
            if (EnemyDebug.Overlay) Markers();
            if (_open) Panel();
            else if (EnemyDebug.Overlay) Corner();
            ImUi.End();
        }

        /// <summary>A marker on each enemy, through walls: where it is (red) and where it shows (yellow).</summary>
        static void Markers()
        {
            var cam = Camera.main;
            if (!cam) return;
            s_behind = 0;
            foreach (var e in EnemyDebug.All)
            {
                if (!e.DevPresent) continue;
                float d = Vector3.Distance(cam.transform.position, e.DevPosition);
                Mark(cam, e.DevPosition + Vector3.up * 1.1f, $"{e.DevName} {d:0.0} m", new Color(1f, 0.25f, 0.2f, 1f));
                if (e.DevShownAt.HasValue) Mark(cam, e.DevShownAt.Value, "shows here", ImUi.Accent);
            }
        }

        static int s_behind;

        static void Mark(Camera cam, Vector3 world, string label, Color c)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            bool behind = s.z < 0f;
            float x = s.x / ImUi.Scale, y = (Screen.height - s.y) / ImUi.Scale;
            if (behind) { x = 20f; y = ImUi.Height - 40f - 26f * s_behind++; label += " (behind you)"; }
            x = Mathf.Clamp(x, 12f, ImUi.Width - 12f); y = Mathf.Clamp(y, 12f, ImUi.Height - 30f);
            ImUi.Fill(new Rect(x - 6, y - 6, 12, 12), Color.black);
            ImUi.Fill(new Rect(x - 4, y - 4, 8, 8), c);
            var old = GUI.color; GUI.color = c;
            GUI.Label(new Rect(x + 10, y - 12, 360, 24), label, ImUi.Small);
            GUI.color = old;
        }

        /// <summary>With the menu closed: one line per enemy (and the director) in the top-left corner.</summary>
        static void Corner()
        {
            var lines = new List<string>();
            var td = TensionDirector.Instance;
            if (td) lines.Add($"Director: {td.Current} {td.Tension:0.00}{(EnemyDebug.Hold ? " (hands off)" : "")}");
            foreach (var e in EnemyDebug.All) lines.Add($"{e.DevName}: {e.DevStatus}");
            if (!string.IsNullOrEmpty(EnemyDebug.Note) && Time.unscaledTime - EnemyDebug.NoteTime < 8f) lines.Add(EnemyDebug.Note);
            float y = 14f;
            foreach (var l in lines)
            {
                ImUi.Fill(new Rect(10, y, 760, 24), new Color(0, 0, 0, 0.55f));
                GUI.Label(new Rect(16, y + 1, 750, 22), l, ImUi.Small);
                y += 26f;
            }
        }

        static GUIStyle s_wrap;

        void Panel()
        {
            float w = 520f, m = 20f;
            var box = new Rect(ImUi.Width - w - m, m, w, ImUi.Height - m * 2f);
            ImUi.Fill(box, new Color(ImUi.Panel.r, ImUi.Panel.g, ImUi.Panel.b, 0.94f));
            GUI.Label(new Rect(box.x + 22, box.y + 14, w - 44, 40), "ENEMIES", ImUi.Title);
            ImUi.Dimmed(new Rect(box.x + 22, box.y + 54, w - 44, 40), "Developer menu: test the enemies without waiting for them. K or Esc closes it.");

            var view = new Rect(box.x + 22, box.y + 100, w - 36, box.height - 100 - 50);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 18, _contentH));
            float cw = view.width - 22f, y = 0f;
            if (s_wrap == null) s_wrap = new GUIStyle(ImUi.Small) { wordWrap = true };

            ImUi.Header(new Rect(0, y, cw, 26), "Switches"); y += 30f;
            EnemyDebug.Hold = ImUi.Toggle(new Rect(0, y, cw, 28), "Director hands off", EnemyDebug.Hold,
                "The tension director does not send the enemies, make them bold or call them off. Bringing one turns this on."); y += 30f;
            EnemyDebug.NoCatch = ImUi.Toggle(new Rect(0, y, cw, 28), "Can't be caught", EnemyDebug.NoCatch,
                "An enemy that reaches you backs off instead of catching you."); y += 30f;
            EnemyDebug.Freeze = ImUi.Toggle(new Rect(0, y, cw, 28), "Freeze enemies", EnemyDebug.Freeze,
                "They stand still and their anger stays where it is: walk up and look at them."); y += 30f;
            EnemyDebug.Overlay = ImUi.Toggle(new Rect(0, y, cw, 28), "Show on screen", EnemyDebug.Overlay,
                "Their status in the corner and a marker where each one really is (red) and where it shows (yellow), with this menu closed too."); y += 40f;

            var td = TensionDirector.Instance;
            ImUi.Header(new Rect(0, y, cw, 26), "Tension director"); y += 30f;
            if (td)
            {
                string st = $"{td.Current}, tension {td.Tension:0.00}, last: {td.LastEvent}" + (td.Active ? "" : " (not running here)");
                float h = s_wrap.CalcHeight(new GUIContent(st), cw);
                ImUi.Dimmed(new Rect(0, y, cw, h + 4), st); y += h + 8f;
                _actions.Clear();
                _actions.Add(new DevAction("Peak now", () => Said(td.Force("peak"), "The director: peak."), "Jump to the director's peak (with the director hands off, the enemies are left alone)."));
                _actions.Add(new DevAction("A room goes dark", () => Said(td.Force("dark"), "A nearby room went dark."), null));
                _actions.Add(new DevAction("Lights flicker", () => Said(td.Force("flicker"), "Your lights flickered."), null));
                _actions.Add(new DevAction("A door swings shut", () => Said(td.Force("door"), "A door out of sight swung shut."), null));
                y = Buttons(_actions, y, cw) + 10f;
            }

            // The dark changing things while you are away (DarkChanges).
            var dc = DarkChanges.Instance;
            if (dc)
            {
                ImUi.Header(new Rect(0, y, cw, 26), "The dark"); y += 30f;
                string dst = $"{DarkChanges.Count} changed so far; last: {DarkChanges.LastEvent}";
                float dh = s_wrap.CalcHeight(new GUIContent(dst), cw);
                ImUi.Dimmed(new Rect(0, y, cw, dh + 4), dst); y += dh + 8f;
                _actions.Clear();
                _actions.Add(new DevAction("Something moves", () => Said(dc.Force(false), "Something in a dark room out of sight was moved."),
                    "A piece of furniture in a dark room out of sight (11 m or more away) is moved or knocked over now."));
                _actions.Add(new DevAction("You hear it move", () => Said(dc.Force(true), "Something out of sight is being dragged."),
                    "The same within earshot (6-26 m): slowly, with the sound of it."));
                _actions.Add(new DevAction("Even in the light", () => Said(dc.Force(true, true), "Something out of sight is being dragged (lit room)."),
                    "As above, but the room need not be dark (to try it anywhere)."));
                y = Buttons(_actions, y, cw) + 10f;
            }

            foreach (var e in EnemyDebug.All)
            {
                ImUi.Header(new Rect(0, y, cw, 26), e.DevName); y += 30f;
                string st = e.DevStatus;
                float h = s_wrap.CalcHeight(new GUIContent(st), cw);
                ImUi.Dimmed(new Rect(0, y, cw, h + 4), st); y += h + 8f;
                _actions.Clear();
                var who = e;
                if (e.DevPresent) _actions.Add(new DevAction("Turn to face it", () => Face(who), "Turns you toward where it shows (its shadow), or where it is."));
                e.DevActions(_actions);
                y = Buttons(_actions, y, cw) + 10f;
            }
            if (EnemyDebug.All.Count == 0) { ImUi.Dimmed(new Rect(0, y, cw, 30), "No enemies."); y += 34f; }
            _contentH = y + 10f;
            GUI.EndScrollView();

            if (!string.IsNullOrEmpty(EnemyDebug.Note))
                ImUi.Dimmed(new Rect(box.x + 22, box.yMax - 44, w - 44, 40), EnemyDebug.Note);
            ImUi.DrawTooltip();
        }

        static void Face(IDevEnemy e)
        {
            var p = FindAnyObjectByType<CamcorderPlayerController>();
            if (!p) return;
            Vector3 d = (e.DevShownAt ?? e.DevPosition) - p.transform.position; d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            p.SetYaw(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }

        static void Said(bool ok, string note) => EnemyDebug.Say(ok ? note : "Nothing happened (nowhere for it here?).");

        /// <summary>Two buttons a row; a header takes a row of its own.</summary>
        static float Buttons(List<DevAction> actions, float y, float cw)
        {
            float bw = (cw - 8f) * 0.5f;
            int col = 0;
            foreach (var a in actions)
            {
                if (a.run == null)
                {
                    if (col != 0) { y += 36f; col = 0; }
                    GUI.Label(new Rect(0, y + 2, cw, 22), a.label, ImUi.Small); y += 26f;
                    continue;
                }
                if (ImUi.Button(new Rect(col * (bw + 8f), y, bw, 32), a.label, false, a.tip)) a.run();
                if (++col == 2) { col = 0; y += 36f; }
            }
            if (col != 0) y += 36f;
            return y;
        }
    }
}
