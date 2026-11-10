using System;
using System.Collections;
using UnityEngine;

namespace Backrooms.Menu
{
    /// <summary>
    /// The blue screen of a camcorder between tapes: "PLAY ►" or "STOP ■" in the corner while the world scene loads, then
    /// a quick fade into the new picture once it has settled. Lives across the scene load.
    /// </summary>
    public class TapeTransition : MonoBehaviour
    {
        static TapeTransition s_active;
        public static bool Running => s_active;

        string _label;
        Action _load;
        float _alpha = 1f;
        GUIStyle _big;
        Texture2D _blue;

        /// <summary>Shows the blue screen, then runs the load (a scene load) behind it.</summary>
        public static void Run(string label, Action load)
        {
            if (s_active) { load?.Invoke(); return; }
            var go = new GameObject("TapeTransition");
            DontDestroyOnLoad(go);
            s_active = go.AddComponent<TapeTransition>();
            s_active._label = label;
            s_active._load = load;
        }

        IEnumerator Start()
        {
            // Draw the blue screen for a couple of frames before the load freezes the game.
            yield return null;
            yield return null;
            var load = _load; _load = null;
            load?.Invoke();
            // The new scene builds its world in its first frames: stay up until they are done.
            for (int i = 0; i < 6; i++) yield return null;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.45f) yield return null;
            while (_alpha > 0f) { _alpha -= Time.unscaledDeltaTime / 0.35f; yield return null; }
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (s_active == this) s_active = null;
            if (_blue) Destroy(_blue);
        }

        void OnGUI()
        {
            if (_big == null)
            {
                // The camcorder's own face (UiKit): its blue screen.
                _big = new GUIStyle(GUI.skin.label) { font = Backrooms.UI.UiKit.Font(Backrooms.UI.UiKit.Face.Cam, true), fontSize = 64, alignment = TextAnchor.UpperLeft };
                _blue = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _blue.SetPixel(0, 0, new Color(0.05f, 0.12f, 0.62f)); _blue.Apply();
            }
            GUI.depth = -1000;
            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_alpha));
            GUI.DrawTexture(new Rect(0, 0, w, 1080), _blue);
            Shadowed(new Rect(w * 0.06f + 20, 80, 900, 90), _label, _big);
            GUI.color = old;
            GUI.matrix = Matrix4x4.identity;
        }

        static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var c = GUI.color;
            style.normal.textColor = new Color(0f, 0f, 0.1f, 0.8f * c.a);
            GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), text, style);
            style.normal.textColor = new Color(1f, 1f, 1f, c.a);
            GUI.Label(r, text, style);
        }
    }
}
