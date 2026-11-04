using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// A cassette deck and a boombox's speakers on the sound of the tape (2026-11-04), on the audio thread before the
    /// source is placed in the world (so the camcorder's own sound chain comes after it, as for any sound in the world).
    /// Modelled the way tape emulators are (a modulated delay line, then the tape's and the speaker's frequency
    /// response, saturation, noise): flutter (8.3 Hz capstan, a little 23 Hz scrape) as the delay wavering (the slow
    /// wow is the source's pitch, from the deck); a cheap type-I playback: rolled off above ~9 kHz, a head bump round
    /// 110 Hz; a 4 in speaker: nothing much under 90 Hz, a presence lift round 2.6 kHz; soft tape saturation; tape hiss
    /// (-52 dB, brighter than pink) while the tape runs; the motor's faint whir; now and then a dropout (worn oxide: a
    /// dip of 3-9 dB, duller, a tenth of a second). Specs from boombox decks: 40-12,500 Hz, ~55 dB S/N, ~0.15 % W&amp;F.
    /// The level (the volume knob) is applied here, so the hiss turns with it.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class TapeDeckFilter : MonoBehaviour
    {
        /// <summary>The tape is moving at play speed (hiss, the motor's whir).</summary>
        public bool running;
        /// <summary>The volume knob, 0..1.</summary>
        public float level = 0.45f;
        /// <summary>How worn the tape is (more dropouts, more hiss), 0..1.</summary>
        public float wear = 0.35f;

        int _sr = 48000;
        double _t;
        readonly float[] _buf = new float[8192];
        int _w;
        uint _seed = 0x9E3779B9u;
        Biquad _hp, _bump, _pres, _lp, _lp2, _hissHp, _hissLp, _dull;
        float _drop = 1f, _dropTarget = 1f, _dropUntil, _nextDrop = 12f, _dull1 = 1f;
        float _scrape;

        struct Biquad
        {
            float b0, b1, b2, a1, a2, z1, z2;
            public float Run(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
            static Biquad Norm(double b0, double b1, double b2, double a0, double a1, double a2) => new Biquad
            { b0 = (float)(b0 / a0), b1 = (float)(b1 / a0), b2 = (float)(b2 / a0), a1 = (float)(a1 / a0), a2 = (float)(a2 / a0) };
            // RBJ cookbook
            public static Biquad LowPass(float sr, float f, float q)
            {
                double w = 2 * System.Math.PI * f / sr, c = System.Math.Cos(w), a = System.Math.Sin(w) / (2 * q);
                return Norm((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
            }
            public static Biquad HighPass(float sr, float f, float q)
            {
                double w = 2 * System.Math.PI * f / sr, c = System.Math.Cos(w), a = System.Math.Sin(w) / (2 * q);
                return Norm((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
            }
            public static Biquad Peak(float sr, float f, float q, float db)
            {
                double A = System.Math.Pow(10, db / 40), w = 2 * System.Math.PI * f / sr, c = System.Math.Cos(w), a = System.Math.Sin(w) / (2 * q);
                return Norm(1 + a * A, -2 * c, 1 - a * A, 1 + a / A, -2 * c, 1 - a / A);
            }
        }

        void Awake()
        {
            _sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            _hp = Biquad.HighPass(_sr, 90f, 0.75f);
            _bump = Biquad.Peak(_sr, 110f, 0.9f, 3f);
            _pres = Biquad.Peak(_sr, 2600f, 1.1f, 2f);
            _lp = Biquad.LowPass(_sr, 9200f, 0.7f);
            _lp2 = Biquad.LowPass(_sr, 11500f, 0.6f);
            _hissHp = Biquad.HighPass(_sr, 1800f, 0.7f);
            _hissLp = Biquad.LowPass(_sr, 9000f, 0.7f);
            _dull = Biquad.LowPass(_sr, 2400f, 0.7f);
            _seed ^= (uint)GetInstanceID();
        }

        float Noise()
        {
            _seed ^= _seed << 13; _seed ^= _seed >> 17; _seed ^= _seed << 5;
            return (_seed & 0xFFFFFF) / (float)0x800000 - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0) return;
            float dt = 1f / _sr;
            float lvl = Mathf.Clamp01(level) * 1.35f * GameSettings.MusicLevel;     // (music's volume: Settings)
            bool run = running;
            float hiss = run ? (0.0025f + 0.0015f * wear) : 0f;
            int frames = data.Length / channels;
            const float Base = 0.006f;                                  // the delay's middle (s)
            for (int f = 0; f < frames; f++)
            {
                float x = 0f;
                for (int c = 0; c < channels; c++) x += data[f * channels + c];
                x /= channels;
                // flutter: the delay wavering (capstan 8.3 Hz, a little scrape at 23 Hz drifting)
                _t += dt;
                _scrape += (Noise() - _scrape) * 0.002f;
                float d = Base + 0.000012f * Mathf.Sin((float)(_t * 2 * System.Math.PI * 8.3)) + (0.000004f + 0.000004f * _scrape) * Mathf.Sin((float)(_t * 2 * System.Math.PI * 23.0));
                _buf[_w] = x;
                float rp = _w - d * _sr;
                int len = _buf.Length;
                while (rp < 0) rp += len;
                int i0 = (int)rp; float fr = rp - i0;
                float y = _buf[i0 % len] * (1f - fr) + _buf[(i0 + 1) % len] * fr;
                _w = (_w + 1) % len;
                // dropouts: a dip, duller, for a tenth of a second or so
                if (run)
                {
                    _nextDrop -= dt;
                    if (_nextDrop <= 0f)
                    {
                        _nextDrop = (14f + 30f * (1f - wear)) * (0.4f + 1.2f * (Noise() * 0.5f + 0.5f));
                        _dropTarget = Mathf.Pow(10f, -(3f + 6f * (Noise() * 0.5f + 0.5f)) / 20f);
                        _dropUntil = (float)_t + 0.04f + 0.12f * (Noise() * 0.5f + 0.5f);
                    }
                    if (_t > _dropUntil) _dropTarget = 1f;
                }
                _drop += (_dropTarget - _drop) * 0.004f;
                float dull = _dull.Run(y);
                _dull1 = Mathf.Lerp(_dull1, _drop < 0.97f ? 0.6f : 0f, 0.002f);
                y = Mathf.Lerp(y, dull, _dull1) * _drop;
                // the tape's response, its hiss, the saturation, then the speaker's
                y = _lp2.Run(_lp.Run(_bump.Run(y)));
                y += _hissLp.Run(_hissHp.Run(Noise())) * hiss;
                if (run) y += (Mathf.Sin((float)(_t * 2 * System.Math.PI * 47.0)) * 0.3f + Noise() * 0.7f) * 0.0004f;     // the motor's whir
                y = Sat(y * 1.1f) / 1.1f;
                y = _pres.Run(_hp.Run(y)) * lvl;
                for (int c = 0; c < channels; c++) data[f * channels + c] = y;
            }
        }

        static float Sat(float x)
        {
            // tanh-like, a touch asymmetric (tape's even harmonics)
            x += 0.04f * x * x;
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }
    }
}
