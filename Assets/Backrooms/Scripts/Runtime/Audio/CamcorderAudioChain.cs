using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// The camcorder's recording path, applied to everything the AudioListener hears (it sits on the
    /// listener after the room reverb filter). Order inside the chain:
    ///   stereo width -> mic self-noise + handling noise -> mic coloration (bass cut, soft highs)
    ///   -> automatic gain control -> gentle compression -> restrained saturation -> output volume.
    /// AGC and compression are stereo-linked so the image never leans. A processed / unprocessed mix is
    /// crossfaded (never switched), and every parameter is smoothed on the audio thread, so changes do
    /// not click. Sources with bypassListenerEffects (UI) skip this chain entirely.
    ///
    /// Thread safety: the main thread only writes the public float fields below; the audio thread reads
    /// them and keeps all of its own state in preallocated fields (no allocations in the callback).
    /// Audio gain is independent of the visual auto-exposure.
    /// </summary>
    public class CamcorderAudioChain : MonoBehaviour
    {
        // ---- Parameters (written on the main thread) ----
        [Header("Player settings (from CamcorderSettings)")]
        [Range(0f, 1f)] public float outputVolume = 0.8f;
        public bool processing = true;
        [Tooltip("Temporarily listen to the unprocessed sound (not saved).")]
        public bool compareUnprocessed;
        [Range(0f, 1f)] public float coloration = 0.6f;
        [Range(0f, 1f)] public float autoGain = 0.6f;
        [Range(0f, 1f)] public float hiss = 0.5f;
        [Range(0f, 1f)] public float handling = 0.5f;
        [Range(0f, 1.5f)] public float stereoWidth = 0.8f;

        [Header("Developer")]
        public float agcTargetDb = -30f;
        public float agcMaxBoostDb = 15f;
        public float agcMaxCutDb = 12f;
        [Tooltip("Seconds for the gain to drop when sound gets louder.")]
        public float agcAttack = 0.12f;
        [Tooltip("Seconds for the gain to recover when it gets quieter.")]
        public float agcRelease = 3.5f;
        [Tooltip("Fastest the gain may rise, in dB per second.")]
        public float agcMaxRiseDbPerSec = 4f;
        [Range(0f, 1f)] public float compression = 0.4f;
        [Range(0f, 1f)] public float saturation = 0.3f;

        // ---- Live inputs from gameplay ----
        [System.NonSerialized] public float handlingMotion;   // 0..1, from walking speed
        int _stepRequests;                                    // incremented by the main thread per footstep
        public void HandlingImpulse() { System.Threading.Interlocked.Increment(ref _stepRequests); }

        // ---- Meters (written on the audio thread) ----
        public float InputDb { get; private set; } = -120f;
        public float OutputDb { get; private set; } = -120f;
        public float GainDb { get; private set; }
        public float ProcessedMix { get; private set; } = 1f;

        // ---- Audio-thread state ----
        int _rate = 48000;
        uint _rng = 0x9E3779B9u;
        float _mix = 1f, _vol = 0.8f, _width = 0.8f;
        float _hp = 20f, _lp = 20000f;
        float _hissLevel, _handLevel, _handMotion;
        float _env, _cEnv, _gainDb;
        int _stepsSeen; float _thumpT = 10f, _thumpAmp;
        float _rum1, _rum2;
        // Biquads per channel: high-pass then low-pass.
        float _hb0, _hb1, _hb2, _ha1, _ha2, _lb0, _lb1, _lb2, _la1, _la2;
        float _hz1L, _hz2L, _hz1R, _hz2R, _lz1L, _lz2L, _lz1R, _lz2R;
        float _sumIn, _sumOut; int _meterN;

        void Awake() { _rate = AudioSettings.outputSampleRate; _gainDb = 0f; }

        float Rand()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 8388608f - 1f;
        }

        static void Coefs(float hz, int rate, bool high, out float b0, out float b1, out float b2, out float a1, out float a2)
        {
            float w = 6.2831853f * Mathf.Min(hz, rate * 0.45f) / rate;
            float cs = Mathf.Cos(w), al = Mathf.Sin(w) * 0.70710678f;   // Q = 0.707
            float a0 = 1f + al;
            if (high) { b0 = (1 + cs) * 0.5f; b1 = -(1 + cs); b2 = b0; }
            else { b0 = (1 - cs) * 0.5f; b1 = 1 - cs; b2 = b0; }
            b0 /= a0; b1 /= a0; b2 /= a0; a1 = -2f * cs / a0; a2 = (1f - al) / a0;
        }

        static float Db(float lin) => 20f * Mathf.Log10(Mathf.Max(lin, 1e-6f));

        void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            if (frames == 0) return;
            float dt = 1f / _rate;

            // Per-block smoothing of filter cutoffs (log domain), then fresh coefficients.
            float c = Mathf.Clamp01(coloration);
            float hpT = Mathf.Lerp(20f, 210f, c), lpT = Mathf.Lerp(20000f, 6200f, c);
            float blockK = 1f - Mathf.Exp(-frames * dt / 0.08f);
            _hp = Mathf.Exp(Mathf.Lerp(Mathf.Log(_hp), Mathf.Log(hpT), blockK));
            _lp = Mathf.Exp(Mathf.Lerp(Mathf.Log(_lp), Mathf.Log(lpT), blockK));
            Coefs(_hp, _rate, true, out _hb0, out _hb1, out _hb2, out _ha1, out _ha2);
            Coefs(_lp, _rate, false, out _lb0, out _lb1, out _lb2, out _la1, out _la2);

            float mixT = processing && !compareUnprocessed ? 1f : 0f;
            float volT = Mathf.Clamp01(outputVolume);
            float widthT = Mathf.Clamp(stereoWidth, 0f, 1.5f);
            float hissT = Mathf.Clamp01(hiss) * 0.0022f;
            float handT = Mathf.Clamp01(handling);
            float motionT = Mathf.Clamp01(handlingMotion);
            float strength = Mathf.Clamp01(autoGain);
            float sk = 1f - Mathf.Exp(-dt / 0.03f);                  // 30 ms parameter smoothing
            float envAtt = 1f - Mathf.Exp(-dt / 0.006f), envRel = 1f - Mathf.Exp(-dt / 0.25f);
            float gAtt = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, agcAttack));
            float gRel = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, agcRelease));
            float maxRise = agcMaxRiseDbPerSec * dt;
            float cAtt = 1f - Mathf.Exp(-dt / 0.005f), cRel = 1f - Mathf.Exp(-dt / 0.15f);
            float ratio = 1f + 3f * Mathf.Clamp01(compression), thr = -20f;
            float drive = 1f + 2.5f * Mathf.Clamp01(saturation);
            float rumK = 1f - Mathf.Exp(-dt * 6.2831853f * 90f);

            int steps = _stepRequests;
            if (steps != _stepsSeen) { _stepsSeen = steps; _thumpT = 0f; _thumpAmp = 0.7f + 0.3f * Mathf.Abs(Rand()); }

            for (int f = 0; f < frames; f++)
            {
                int i = f * channels;
                float inL = data[i], inR = channels > 1 ? data[i + 1] : inL;
                _sumIn += inL * inL + inR * inR;

                _mix += (mixT - _mix) * sk; _vol += (volT - _vol) * sk; _width += (widthT - _width) * sk;
                _hissLevel += (hissT - _hissLevel) * sk; _handLevel += (handT - _handLevel) * sk;
                _handMotion += (motionT - _handMotion) * (sk * 0.2f);

                // Stereo width (mid/side).
                float mid = (inL + inR) * 0.5f, side = (inL - inR) * 0.5f * _width;
                float l = mid + side, r = mid - side;

                // Mic self-noise (decorrelated) and handling noise (body of the camera, centred).
                l += Rand() * _hissLevel; r += Rand() * _hissLevel;
                float w = Rand();
                _rum1 += (w - _rum1) * rumK; _rum2 += (_rum1 - _rum2) * rumK;
                float hand = _rum2 * 0.035f * _handMotion;
                if (_thumpT < 0.12f)
                {
                    hand += Mathf.Sin(6.2831853f * 58f * _thumpT) * Mathf.Exp(-_thumpT / 0.035f) * 0.012f * _thumpAmp;
                    _thumpT += dt;
                }
                hand *= _handLevel;
                l += hand; r += hand;

                // Coloration: high-pass then low-pass (transposed direct form II).
                float y;
                y = _hb0 * l + _hz1L; _hz1L = _hb1 * l - _ha1 * y + _hz2L; _hz2L = _hb2 * l - _ha2 * y; l = y;
                y = _hb0 * r + _hz1R; _hz1R = _hb1 * r - _ha1 * y + _hz2R; _hz2R = _hb2 * r - _ha2 * y; r = y;
                y = _lb0 * l + _lz1L; _lz1L = _lb1 * l - _la1 * y + _lz2L; _lz2L = _lb2 * l - _la2 * y; l = y;
                y = _lb0 * r + _lz1R; _lz1R = _lb1 * r - _la1 * y + _lz2R; _lz2R = _lb2 * r - _la2 * y; r = y;

                // AGC: linked RMS-ish envelope; gain drops quickly for loud sounds, recovers slowly and
                // rate-limited, and is bounded so silence can never run away.
                float p = 0.5f * (l * l + r * r);
                _env += (p - _env) * (p > _env ? envAtt : envRel);
                if ((f & 15) == 0)
                {
                    float levelDb = 10f * Mathf.Log10(Mathf.Max(_env, 1e-12f));
                    float want = Mathf.Clamp(agcTargetDb - levelDb, -agcMaxCutDb, agcMaxBoostDb) * strength;
                    float step16 = 16f;
                    if (want < _gainDb) _gainDb += (want - _gainDb) * Mathf.Min(1f, gAtt * step16);
                    else _gainDb += Mathf.Min((want - _gainDb) * Mathf.Min(1f, gRel * step16), maxRise * step16);
                }
                float g = Mathf.Pow(10f, _gainDb * 0.05f);
                l *= g; r *= g;

                // Gentle linked compression above -20 dBFS.
                float pk = Mathf.Max(Mathf.Abs(l), Mathf.Abs(r));
                _cEnv += (pk - _cEnv) * (pk > _cEnv ? cAtt : cRel);
                float over = Db(_cEnv) - thr;
                if (over > 0f)
                {
                    float gr = Mathf.Pow(10f, -over * (1f - 1f / ratio) * 0.05f);
                    l *= gr; r *= gr;
                }

                // Restrained saturation (unity gain for quiet signals).
                l = Tanh(l * drive) / drive; r = Tanh(r * drive) / drive;

                // Crossfade processed / unprocessed, then output volume and a safety clip.
                float outL = (inL + (l - inL) * _mix) * _vol;
                float outR = (inR + (r - inR) * _mix) * _vol;
                outL = Mathf.Clamp(outL, -0.99f, 0.99f); outR = Mathf.Clamp(outR, -0.99f, 0.99f);
                _sumOut += outL * outL + outR * outR;

                data[i] = outL;
                if (channels > 1) data[i + 1] = outR;
                for (int ch = 2; ch < channels; ch++) data[i + ch] *= _vol;   // surround extras: volume only
            }

            _meterN += frames * 2;
            if (_meterN >= _rate / 5)
            {
                InputDb = 10f * Mathf.Log10(Mathf.Max(_sumIn / _meterN, 1e-12f));
                OutputDb = 10f * Mathf.Log10(Mathf.Max(_sumOut / _meterN, 1e-12f));
                GainDb = _gainDb; ProcessedMix = _mix;
                _sumIn = _sumOut = 0f; _meterN = 0;
            }
        }

        static float Tanh(float x)
        {
            if (x > 4f) return 1f; if (x < -4f) return -1f;
            float e = Mathf.Exp(2f * x);
            return (e - 1f) / (e + 1f);
        }
    }
}
