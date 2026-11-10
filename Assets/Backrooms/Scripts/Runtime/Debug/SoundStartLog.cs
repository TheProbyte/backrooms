using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Lists every sound as it starts (2026-11-09, the owner: "odd sounds when the place loads, like clicks"): each frame,
    /// any AudioSource newly playing, or a one-shot voice whose clip changed, with the time, clip, loudness at the ear
    /// and the object. Add in play mode; read <see cref="Text"/>. Debug only.
    /// </summary>
    public class SoundStartLog : MonoBehaviour
    {
        public float since;
        readonly Dictionary<AudioSource, (AudioClip clip, float time)> _seen = new Dictionary<AudioSource, (AudioClip, float)>();
        readonly StringBuilder _sb = new StringBuilder();
        public string Text => _sb.ToString();
        public int Count;

        void OnEnable() { since = Time.unscaledTime; }

        public void Clear() { _sb.Clear(); Count = 0; since = Time.unscaledTime; }

        void LateUpdate()
        {
            var ear = FindAnyObjectByType<AudioListener>();
            foreach (var s in FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                bool playing = s.isPlaying;
                _seen.TryGetValue(s, out var was);
                bool started = playing && (was.clip != s.clip || s.time < was.time - 0.01f || was.clip == null);
                _seen[s] = playing ? (s.clip, s.time) : (null, 0f);
                if (!started || !s.clip) continue;
                float d = ear ? Vector3.Distance(ear.transform.position, s.transform.position) : 0f;
                float att = s.spatialBlend < 0.5f ? 1f : Mathf.Clamp01(s.minDistance / Mathf.Max(s.minDistance, d));
                Count++;
                if (_sb.Length < 60000)
                    _sb.AppendLine($"{Time.unscaledTime - since,6:0.00}s {s.clip.name,-28} vol {s.volume:0.000} at-ear~{s.volume * att:0.000} d {d,5:0.0} loop {(s.loop ? 1 : 0)} {Path(s.transform)}");
            }
        }

        static string Path(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 2 && t.parent; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
