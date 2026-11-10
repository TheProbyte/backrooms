using UnityEngine;

namespace Backrooms.UI
{
    /// <summary>
    /// The interface's five sounds (Docs/UiPlan.md U19, 2026-11-10), in the camcorder's voice (a small square-wave beep
    /// through its speaker, as its menu's): every menu, F5, the pause menu and the hub's boards use these, so a sound means
    /// the same thing everywhere. As game menus' sounds are told apart: a move is the lightest tick; a select rises; back
    /// falls; a refusal is a low buzz, twice; a confirm (paid, ordered, done) is a brighter rising chirp.
    /// </summary>
    public enum UiSound { Move, Select, Back, Refuse, Confirm }

    public static class UiSounds
    {
        static readonly AudioClip[] _clips = new AudioClip[5];
        static AudioSource _src;
        const int Rate = 44100;

        /// <summary>Plays a sound flat (not in the world), heard while the game is paused too.</summary>
        public static void Play(UiSound s)
        {
            if (!_src)
            {
                var go = new GameObject("UiSounds");
                Object.DontDestroyOnLoad(go);
                _src = go.AddComponent<AudioSource>();
                _src.playOnAwake = false; _src.spatialBlend = 0f; _src.ignoreListenerPause = true;
            }
            _src.PlayOneShot(Clip(s), 0.45f * Audio.SoundCatalog.Volume(Audio.SoundIds.MenuBeep));
        }

        /// <summary>The clip, for a source of one's own (a board's speaker in the world).</summary>
        public static AudioClip Clip(UiSound s)
        {
            int i = (int)s;
            if (_clips[i]) return _clips[i];
            switch (s)
            {
                case UiSound.Move: return _clips[i] = Make("UiMove", (0.035f, 1950f, 1950f));
                case UiSound.Select: return _clips[i] = Make("UiSelect", (0.035f, 1950f, 1950f), (0.04f, 2460f, 2460f));
                case UiSound.Back: return _clips[i] = Make("UiBack", (0.035f, 1640f, 1640f), (0.04f, 1230f, 1230f));
                case UiSound.Refuse: return _clips[i] = Make("UiRefuse", (0.07f, 330f, 300f), (0.035f, 0f, 0f), (0.07f, 330f, 300f));
                default: return _clips[i] = Make("UiConfirm", (0.045f, 1460f, 1460f), (0.045f, 1950f, 1950f), (0.08f, 2460f, 2460f));
            }
        }

        /// <summary>Notes in a row (length, start and end frequency; 0 Hz a gap), square-ish and softened at the edges.</summary>
        static AudioClip Make(string name, params (float len, float f0, float f1)[] notes)
        {
            float total = 0f; foreach (var n in notes) total += n.len;
            int count = Mathf.CeilToInt(total * Rate);
            var d = new float[count];
            int at = 0; float phase = 0f, lp = 0f;
            foreach (var (len, f0, f1) in notes)
            {
                int n = Mathf.RoundToInt(len * Rate);
                for (int k = 0; k < n && at < count; k++, at++)
                {
                    float t = k / (float)Rate;
                    if (f0 <= 0f) { d[at] = 0f; continue; }
                    float f = Mathf.Lerp(f0, f1, t / len);
                    phase += f / Rate;
                    float sq = Mathf.Clamp(Mathf.Sin(phase * Mathf.PI * 2f) * 3f, -1f, 1f);
                    lp += (sq - lp) * 0.45f;                                   // the small speaker's top end rolled off
                    float env = Mathf.Min(1f, t / 0.002f) * Mathf.Min(1f, (len - t) / 0.005f);
                    d[at] = lp * env * 0.32f;
                }
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
