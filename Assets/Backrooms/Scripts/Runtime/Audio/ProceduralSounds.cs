using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// Builds every game sound at load time from code (no imported or licensed audio).
    /// Loops are long (7-29 s), use whole cycles of every periodic part, and crossfade their noise
    /// tail into the head, so they loop without clicks and without an obvious short repeat.
    /// </summary>
    public static class ProceduralSounds
    {
        public const int Rate = 44100;
        const float TwoPi = 6.2831853f;

        // ---------------- Small DSP helpers (offline only) ----------------

        struct Biquad
        {
            float b0, b1, b2, a1, a2, z1, z2;
            public static Biquad LowPass(float hz, float q = 0.707f) => Make(hz, q, 0);
            public static Biquad HighPass(float hz, float q = 0.707f) => Make(hz, q, 1);
            public static Biquad BandPass(float hz, float q) => Make(hz, q, 2);
            static Biquad Make(float hz, float q, int type)
            {
                float w = TwoPi * Mathf.Min(hz, Rate * 0.45f) / Rate, cs = Mathf.Cos(w), al = Mathf.Sin(w) / (2f * q);
                float a0 = 1f + al; var b = new Biquad();
                switch (type)
                {
                    case 0: b.b0 = (1 - cs) * 0.5f; b.b1 = 1 - cs; b.b2 = (1 - cs) * 0.5f; break;
                    case 1: b.b0 = (1 + cs) * 0.5f; b.b1 = -(1 + cs); b.b2 = (1 + cs) * 0.5f; break;
                    default: b.b0 = al; b.b1 = 0; b.b2 = -al; break;
                }
                b.b0 /= a0; b.b1 /= a0; b.b2 /= a0; b.a1 = -2f * cs / a0; b.a2 = (1f - al) / a0;
                return b;
            }
            public float Run(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }

        static float White(System.Random r) => (float)r.NextDouble() * 2f - 1f;

        /// <summary>Copies a buffer of n + fade samples into a seamless loop of n samples.</summary>
        static float[] Seamless(float[] raw, int n, int fade, int channels)
        {
            var data = new float[n * channels];
            for (int i = 0; i < n; i++)
            for (int c = 0; c < channels; c++)
            {
                float v = raw[i * channels + c];
                if (i < fade)
                {
                    float w = Mathf.SmoothStep(0f, 1f, i / (float)fade);
                    v = v * w + raw[(n + i) * channels + c] * (1f - w);
                }
                data[i * channels + c] = v;
            }
            return data;
        }

        static AudioClip Clip(string name, float[] data, int channels)
        {
            var clip = AudioClip.Create(name, data.Length / channels, channels, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 1e-6f;
            for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }

        // ---------------- Fluorescent hum ----------------

        /// <summary>
        /// One of several hum characters (variant 0-3): ballast hum at twice mains frequency with
        /// harmonics, lamination buzz, and a faint arc sizzle amplitude-modulated at 120 Hz.
        /// </summary>
        public static AudioClip FluorescentHum(int variant, float mainsHz = 60f)
        {
            float[] seconds = { 9f, 11f, 13f, 7f };
            float L = seconds[variant & 3];
            int n = (int)(Rate * L), fade = Rate / 2;
            var raw = new float[n + fade];
            var rng = new System.Random(1000 + variant * 77);
            float f0 = mainsHz * 2f;
            float[][] sets =
            {
                new[] { 1f, 0.42f, 0.22f, 0.12f, 0.07f, 0.05f },
                new[] { 1f, 0.30f, 0.34f, 0.08f, 0.10f, 0.03f },
                new[] { 0.85f, 0.55f, 0.18f, 0.16f, 0.05f, 0.06f },
                new[] { 1f, 0.25f, 0.14f, 0.20f, 0.04f, 0.08f },
            };
            float[] amps = sets[variant & 3];
            float buzzAmt = new[] { 0.06f, 0.12f, 0.04f, 0.09f }[variant & 3];
            float sizzleAmt = new[] { 0.010f, 0.016f, 0.006f, 0.013f }[variant & 3];
            var ph = new float[amps.Length];
            for (int h = 0; h < amps.Length; h++) ph[h] = (float)rng.NextDouble() * TwoPi;
            // Slow level drift with whole cycles in the loop.
            float d1 = 2f / L, d2 = 5f / L, d3 = 3f / L;
            var buzzLp = Biquad.LowPass(1800f);
            var sizzleBp = Biquad.BandPass(5200f + variant * 900f, 0.9f);
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int h = 0; h < amps.Length; h++) s += amps[h] * Mathf.Sin(TwoPi * f0 * (h + 1) * t + ph[h]);
                float drift = 1f + 0.06f * Mathf.Sin(TwoPi * d1 * t) + 0.04f * Mathf.Sin(TwoPi * d2 * t + 1.3f);
                float buzz = buzzLp.Run(Mathf.Clamp(Mathf.Sin(TwoPi * f0 * t + ph[0]) * 7f, -1f, 1f));
                float am = 0.5f + 0.5f * Mathf.Sin(TwoPi * f0 * t);
                float sizzle = sizzleBp.Run(White(rng)) * am * am * (1f + 0.5f * Mathf.Sin(TwoPi * d3 * t));
                raw[i] = s * 0.5f * drift + buzz * buzzAmt * drift + sizzle * sizzleAmt * 12f;
            }
            var data = Seamless(raw, n, fade, 1);
            Normalize(data, 0.5f);
            return Clip("FluorescentHum_" + variant, data, 1);
        }

        /// <summary>Harsh, crackling buzz of a failing tube (looped; its level follows the stutter).</summary>
        public static AudioClip FaultyBuzz(float mainsHz = 60f)
        {
            float L = 6f;
            int n = (int)(Rate * L), fade = Rate / 4;
            var raw = new float[n + fade];
            var rng = new System.Random(4242);
            float f0 = mainsHz * 2f;
            var lp = Biquad.LowPass(4200f);
            var crackBp = Biquad.BandPass(2600f, 0.6f);
            float crackEnv = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)Rate;
                float sq = Mathf.Clamp(Mathf.Sin(TwoPi * f0 * t) * 14f, -1f, 1f);
                float odd = 0.5f * Mathf.Sin(TwoPi * f0 * 3f * t) + 0.3f * Mathf.Sin(TwoPi * f0 * 5f * t);
                if (rng.NextDouble() < 0.0009) crackEnv = 0.6f + (float)rng.NextDouble() * 0.6f;
                crackEnv *= 0.9965f;
                float crack = crackBp.Run(White(rng)) * crackEnv;
                raw[i] = lp.Run(sq * 0.55f + odd * 0.25f) + crack * 1.4f;
            }
            var data = Seamless(raw, n, fade, 1);
            Normalize(data, 0.5f);
            return Clip("FaultyBuzz", data, 1);
        }

        /// <summary>Starter click, ballast thunk and a short swell of hum (one-shot, variant 0-2).</summary>
        public static AudioClip StrikeOn(int variant, float mainsHz = 60f)
        {
            int n = (int)(Rate * 0.9f);
            var d = new float[n];
            var rng = new System.Random(300 + variant);
            var clickHp = Biquad.HighPass(1800f);
            var thunkLp = Biquad.LowPass(420f);
            float f0 = mainsHz * 2f;
            float clickAt = 0.01f + variant * 0.012f, thunkAt = 0.07f + variant * 0.025f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float c = t >= clickAt ? Mathf.Exp(-(t - clickAt) * 260f) : 0f;
                float click = clickHp.Run(White(rng)) * c;
                float k = t >= thunkAt ? Mathf.Exp(-(t - thunkAt) * 32f) : 0f;
                float thunk = thunkLp.Run(White(rng)) * k * 1.6f + Mathf.Sin(TwoPi * 70f * t) * k * 0.4f;
                float swell = t >= thunkAt ? Mathf.SmoothStep(0f, 1f, (t - thunkAt) / 0.2f) * Mathf.Exp(-(t - thunkAt) * 4f) : 0f;
                float hum = (Mathf.Sin(TwoPi * f0 * t) + 0.4f * Mathf.Sin(TwoPi * f0 * 2 * t)) * swell * 0.35f;
                d[i] = click * 0.8f + thunk + hum;
            }
            for (int i = 0; i < 400; i++) d[n - 1 - i] *= i / 400f;
            Normalize(d, 0.7f);
            return Clip("StrikeOn_" + variant, d, 1);
        }

        /// <summary>Tiny arc tick for stutters (variant 0-3).</summary>
        public static AudioClip Tick(int variant)
        {
            int n = (int)(Rate * 0.06f);
            var d = new float[n];
            var rng = new System.Random(500 + variant);
            var bp = Biquad.BandPass(2400f + variant * 700f, 1.2f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                d[i] = bp.Run(White(rng)) * Mathf.Exp(-t * 140f);
            }
            for (int i = 0; i < 64; i++) d[n - 1 - i] *= i / 64f;
            Normalize(d, 0.6f);
            return Clip("Tick_" + variant, d, 1);
        }

        // ---------------- Beds (stereo) ----------------

        /// <summary>Air handling: low duct rumble plus soft airy diffuser noise, decorrelated L/R.</summary>
        public static AudioClip Ventilation()
        {
            float L = 29f;
            int n = (int)(Rate * L), fade = Rate * 2;
            var raw = new float[(n + fade) * 2];
            var rng = new System.Random(77);
            var lowL = Biquad.LowPass(260f); var lowR = Biquad.LowPass(260f);
            var low2L = Biquad.HighPass(45f); var low2R = Biquad.HighPass(45f);
            var airL = Biquad.BandPass(1500f, 0.5f); var airR = Biquad.BandPass(1700f, 0.5f);
            float m1 = 3f / L, m2 = 7f / L;
            for (int i = 0; i < n + fade; i++)
            {
                float t = i / (float)Rate;
                float common = White(rng);
                float wl = White(rng) * 0.6f + common * 0.4f, wr = White(rng) * 0.6f + common * 0.4f;
                float mod = 1f + 0.12f * Mathf.Sin(TwoPi * m1 * t) + 0.07f * Mathf.Sin(TwoPi * m2 * t + 2f);
                raw[i * 2] = (low2L.Run(lowL.Run(wl)) * 2.2f + airL.Run(White(rng)) * 0.18f) * mod;
                raw[i * 2 + 1] = (low2R.Run(lowR.Run(wr)) * 2.2f + airR.Run(White(rng)) * 0.18f) * mod;
            }
            var data = Seamless(raw, n, fade, 2);
            Normalize(data, 0.5f);
            return Clip("Ventilation", data, 2);
        }

        /// <summary>Very dark, quiet building noise floor, decorrelated L/R.</summary>
        public static AudioClip RoomTone()
        {
            float L = 23f;
            int n = (int)(Rate * L), fade = Rate * 2;
            var raw = new float[(n + fade) * 2];
            var rng = new System.Random(91);
            float bl = 0, br = 0;
            var lpL = Biquad.LowPass(500f); var lpR = Biquad.LowPass(500f);
            var hpL = Biquad.HighPass(28f); var hpR = Biquad.HighPass(28f);
            for (int i = 0; i < n + fade; i++)
            {
                bl = bl * 0.995f + White(rng) * 0.05f;      // brown-ish
                br = br * 0.995f + White(rng) * 0.05f;
                raw[i * 2] = hpL.Run(lpL.Run(bl + White(rng) * 0.01f));
                raw[i * 2 + 1] = hpR.Run(lpR.Run(br + White(rng) * 0.01f));
            }
            var data = Seamless(raw, n, fade, 2);
            Normalize(data, 0.5f);
            return Clip("RoomTone", data, 2);
        }

        /// <summary>The aggregate hum of many distant fixtures: soft, low-passed, with slow beating.</summary>
        public static AudioClip BuildingHum(float mainsHz = 60f)
        {
            float L = 17f;
            int n = (int)(Rate * L), fade = Rate;
            var raw = new float[(n + fade) * 2];
            var rng = new System.Random(55);
            float f0 = mainsHz * 2f;
            var lpL = Biquad.LowPass(700f); var lpR = Biquad.LowPass(700f);
            for (int i = 0; i < n + fade; i++)
            {
                float t = i / (float)Rate;
                // Several fixtures a hair apart in frequency beat slowly (whole cycles in the loop).
                float a = Mathf.Sin(TwoPi * f0 * t) + Mathf.Sin(TwoPi * (f0 + 1f / L) * t + 1f) * 0.8f + Mathf.Sin(TwoPi * (f0 - 2f / L) * t + 2f) * 0.6f;
                float h2 = Mathf.Sin(TwoPi * f0 * 2f * t + 0.5f) * 0.35f + Mathf.Sin(TwoPi * (f0 * 2f + 3f / L) * t) * 0.25f;
                float h3 = Mathf.Sin(TwoPi * f0 * 3f * t + 0.2f) * 0.15f;
                float noise = White(rng) * 0.08f;
                raw[i * 2] = lpL.Run(a + h2 + h3 + noise);
                raw[i * 2 + 1] = lpR.Run(a * 0.95f + h2 * 1.05f + h3 + White(rng) * 0.08f);
            }
            var data = Seamless(raw, n, fade, 2);
            Normalize(data, 0.5f);
            return Clip("BuildingHum", data, 2);
        }

        // ---------------- Footsteps ----------------

        /// <summary>One carpet step (variant 0-7): heel thud through the floor plus a fabric scuff.</summary>
        public static AudioClip CarpetStep(int variant)
        {
            int n = (int)(Rate * 0.32f);
            var d = new float[n];
            var rng = new System.Random(900 + variant * 13);
            float thudHz = 55f + variant * 4.5f, scuffHz = 900f + (variant % 4) * 260f;
            var thudLp = Biquad.LowPass(220f);
            var scuffBp = Biquad.BandPass(scuffHz, 0.7f);
            float scuffAt = 0.015f + (variant % 3) * 0.012f, scuffLen = 0.09f + (variant % 2) * 0.05f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float te = Mathf.Exp(-t * 38f) * Mathf.Min(1f, t * 600f);
                float thud = (Mathf.Sin(TwoPi * thudHz * t) * 0.6f + thudLp.Run(White(rng)) * 1.2f) * te;
                float st = (t - scuffAt) / scuffLen;
                float se = st > 0f && st < 1f ? Mathf.Sin(st * Mathf.PI) * Mathf.Sin(st * Mathf.PI) : 0f;
                float scuff = scuffBp.Run(White(rng)) * se * 0.35f;
                d[i] = thud + scuff;
            }
            for (int i = 0; i < 300; i++) d[n - 1 - i] *= i / 300f;
            Normalize(d, 0.6f);
            return Clip("CarpetStep_" + variant, d, 1);
        }
    }
}
