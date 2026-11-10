using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode's little sounds, made here (no audio files): a soft tick for each snap step while dragging (its pitch
    /// follows the size), a wooden knock for putting something down, a pop for picking it up, a whoosh for deleting, a low
    /// buzz for "that does not fit". Played flat (2D) from the builder's camera.
    /// </summary>
    public static class BuilderSounds
    {
        public enum Sfx { Tick, Place, Pick, Drop, Delete, Grow, Shrink, Error, Wall }

        const int Rate = 44100;
        static AudioClip[] _clips;
        static AudioSource _src;
        static float _lastTick;
        static readonly System.Random _rng = new System.Random(5);

        /// <summary>Plays one (ticks no closer than 25 ms, so a fast drag rattles rather than buzzes).</summary>
        public static void Play(Sfx s, float pitch = 1f, float volume = 1f)
        {
            if (s == Sfx.Tick || s == Sfx.Wall)
            {
                if (Time.unscaledTime - _lastTick < 0.025f) return;
                _lastTick = Time.unscaledTime;
            }
            if (!Ensure()) return;
            _src.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            _src.PlayOneShot(_clips[(int)s], volume * Volume(s));
        }

        static float Volume(Sfx s) => s == Sfx.Tick ? 0.32f : s == Sfx.Wall ? 0.45f : s == Sfx.Error ? 0.35f : 0.6f;

        static bool Ensure()
        {
            if (_src) return true;
            var listener = Object.FindAnyObjectByType<AudioListener>();
            if (!listener) return false;
            _src = listener.gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false; _src.spatialBlend = 0f; _src.bypassReverbZones = true;
            if (_clips == null)
            {
                _clips = new AudioClip[System.Enum.GetValues(typeof(Sfx)).Length];
                _clips[(int)Sfx.Tick] = Make("tick", 0.03f, (t, i) => Knock(t, 2600f, 140f) * 0.8f + Noise() * Env(t, 0.002f, 0.006f) * 0.25f);
                _clips[(int)Sfx.Wall] = Make("wall", 0.07f, (t, i) => Knock(t, 420f, 55f) + Knock(t, 1250f, 90f) * 0.35f + Noise() * Env(t, 0.001f, 0.012f) * 0.3f);
                _clips[(int)Sfx.Place] = Make("place", 0.16f, (t, i) => Knock(t, 165f, 28f) * 0.9f + Knock(t, 410f, 45f) * 0.45f + Knock(t, 980f, 70f) * 0.15f + Noise() * Env(t, 0.001f, 0.01f) * 0.35f);
                _clips[(int)Sfx.Pick] = Make("pick", 0.08f, (t, i) => Mathf.Sin(2f * Mathf.PI * Sweep(t, 0.08f, 520f, 940f)) * Env(t, 0.004f, 0.03f) * 0.7f);
                _clips[(int)Sfx.Drop] = Make("drop", 0.1f, (t, i) => Mathf.Sin(2f * Mathf.PI * Sweep(t, 0.1f, 820f, 380f)) * Env(t, 0.003f, 0.04f) * 0.7f + Knock(t, 200f, 40f) * 0.4f);
                _clips[(int)Sfx.Delete] = Make("delete", 0.26f, (t, i) => Mathf.Sin(2f * Mathf.PI * Sweep(t, 0.26f, 700f, 160f)) * Env(t, 0.005f, 0.11f) * 0.45f + Lowpassed() * Env(t, 0.02f, 0.1f) * 0.5f);
                _clips[(int)Sfx.Grow] = Make("grow", 0.09f, (t, i) => Knock(t, 300f, 40f) * 0.8f + Knock(t, 600f, 60f) * 0.3f);
                _clips[(int)Sfx.Shrink] = Make("shrink", 0.09f, (t, i) => Knock(t, 230f, 40f) * 0.8f + Knock(t, 470f, 60f) * 0.3f);
                _clips[(int)Sfx.Error] = Make("error", 0.18f, (t, i) => Square(t, 150f) * (Env(t, 0.004f, 0.03f) + Env(t - 0.09f, 0.004f, 0.03f)) * 0.3f);
            }
            return true;
        }

        // ---- synthesis ----
        static float _lp;
        static float Noise() => (float)(_rng.NextDouble() * 2.0 - 1.0);
        static float Lowpassed() { _lp += (Noise() - _lp) * 0.08f; return _lp * 3f; }
        static float Env(float t, float attack, float decay) => t < 0f ? 0f : t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);
        /// <summary>A struck resonance: a sine that dies away (rate: how fast, per second).</summary>
        static float Knock(float t, float hz, float rate) => t < 0f ? 0f : Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-t * rate) * Mathf.Min(1f, t / 0.0015f);
        static float Square(float t, float hz) => Mathf.Clamp(Mathf.Sin(2f * Mathf.PI * hz * t) * 3f, -1f, 1f);
        /// <summary>Phase (cycles) of a tone gliding from f0 to f1 over len seconds.</summary>
        static float Sweep(float t, float len, float f0, float f1) { float k = (f1 - f0) / len; return f0 * t + 0.5f * k * t * t; }

        static AudioClip Make(string name, float seconds, System.Func<float, int, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            _lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float fade = Mathf.Clamp01((n - i) / (Rate * 0.004f));        // no click at the end
                data[i] = Mathf.Clamp(f(t, i), -1f, 1f) * fade;
            }
            var clip = AudioClip.Create("builder_" + name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
