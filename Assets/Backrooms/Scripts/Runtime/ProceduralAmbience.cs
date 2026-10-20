using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// Generates seamless looping audio at runtime so the scene needs no imported sound files.
    /// FluorescentHum: 120 Hz ballast hum with harmonics and a faint electrical buzz (3D, per fixture).
    /// RoomTone: soft, dark air/ventilation noise (2D bed).
    /// All frequencies are whole cycles of the loop length, so loops have no click.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProceduralAmbience : MonoBehaviour
    {
        public enum Kind { FluorescentHum, RoomTone }

        public Kind kind = Kind.FluorescentHum;
        [Range(0f, 1f)] public float volume = 0.04f;
        [Tooltip("Mains frequency of the building (60 Hz US, 50 Hz EU). Ballast hum is twice this.")]
        public float mainsHz = 60f;
        [Tooltip("Randomises phase/detune so neighbouring fixtures don't phase perfectly.")]
        public int seed = 1;

        const int SampleRate = 44100;
        const float LoopSeconds = 8f;

        void Start()
        {
            var src = GetComponent<AudioSource>();
            src.clip = kind == Kind.FluorescentHum ? BuildHumClip(mainsHz, seed) : BuildRoomToneClip(seed);
            src.loop = true;
            src.volume = volume;
            src.timeSamples = new System.Random(seed).Next(0, src.clip.samples);
            src.Play();
        }

        public static AudioClip BuildHumClip(float mainsHz, int seed)
        {
            int n = (int)(SampleRate * LoopSeconds);
            var data = new float[n];
            var rng = new System.Random(seed);
            float f0 = mainsHz * 2f;
            // Harmonic amplitudes: fundamental strong, odd/even mix like a magnetic ballast.
            float[] amps = { 1f, 0.42f, 0.22f, 0.12f, 0.07f, 0.05f };
            float[] phases = new float[amps.Length];
            for (int h = 0; h < amps.Length; h++) phases[h] = (float)rng.NextDouble() * 6.283f;
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float s = 0f;
                for (int h = 0; h < amps.Length; h++) s += amps[h] * Mathf.Sin(6.2831853f * f0 * (h + 1) * t + phases[h]);
                // Slow, irregular level drift (whole cycles in the loop: 0.25 Hz and 0.625 Hz).
                float drift = 1f + 0.08f * Mathf.Sin(6.2831853f * 0.25f * t) + 0.05f * Mathf.Sin(6.2831853f * 0.625f * t + 1.3f);
                // Buzz: hard-clipped fundamental, band-limited, very quiet.
                float buzz = Mathf.Clamp(Mathf.Sin(6.2831853f * f0 * t) * 6f, -1f, 1f);
                lp += (buzz - lp) * 0.35f;
                data[i] = (s * 0.5f * drift + lp * 0.06f) * 0.45f;
            }
            var clip = AudioClip.Create("FluorescentHum", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioClip BuildRoomToneClip(int seed)
        {
            int n = (int)(SampleRate * LoopSeconds);
            int fade = SampleRate; // 1 s crossfade for a seamless noise loop
            var raw = new float[n + fade];
            var rng = new System.Random(seed);
            float b0 = 0, b1 = 0, b2 = 0, lp = 0, lp2 = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                // Pink-ish noise, then two low-pass stages: muffled air handling.
                b0 = 0.99765f * b0 + white * 0.0990460f;
                b1 = 0.96300f * b1 + white * 0.2965164f;
                b2 = 0.57000f * b2 + white * 1.0526913f;
                float pink = (b0 + b1 + b2 + white * 0.1848f) * 0.12f;
                lp += (pink - lp) * 0.05f;
                lp2 += (lp - lp2) * 0.08f;
                raw[i] = lp2;
            }
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = raw[i];
                if (i < fade) { float w = i / (float)fade; v = raw[i] * w + raw[n + i] * (1f - w); }
                data[i] = v * 2.2f;
            }
            var clip = AudioClip.Create("RoomTone", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
