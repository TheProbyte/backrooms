using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// Builds every game sound at load time from code (no imported or licensed audio).
    /// Loops are long (7-29 s), use whole cycles of every periodic part, and crossfade their noise
    /// tail into the head, so they loop without clicks and without an obvious short repeat.
    /// </summary>
    public static partial class ProceduralSounds
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

        /// <summary>A camcorder menu's key beep: a short square-ish tone (variant 0: move, 1: choose, 2: back).</summary>
        public static AudioClip MenuBeep(int variant)
        {
            float f = variant == 1 ? 2350f : variant == 2 ? 1500f : 1950f;
            float len = variant == 1 ? 0.075f : 0.04f;
            int n = (int)(Rate * len);
            var d = new float[n];
            var lp = Biquad.LowPass(5200f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float ph = t * (variant == 1 && t > 0.035f ? f * 1.26f : f);           // "choose" rises a third halfway
                float sq = Mathf.Clamp(Mathf.Sin(ph * Mathf.PI * 2f) * 3f, -1f, 1f);
                d[i] = lp.Run(sq) * Mathf.Min(1f, t / 0.002f) * Mathf.Min(1f, (len - t) / 0.006f);
            }
            Normalize(d, 0.35f);
            return Clip("MenuBeep_" + variant, d, 1);
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

        /// <summary>
        /// The Poolrooms (2026-10-31: calmer and quieter, the owner: "more chill, quiet"): the soft low wash of a lot of
        /// still water, a slow breath of it, now and then a faint lap against a tiled edge far off, and every several seconds
        /// a distant drip (a tiny bubble, not a chirp). Decorrelated L/R, 31 s seamless loop. The reverb does the rest.
        /// </summary>
        public static AudioClip PoolAmbience()
        {
            float L = 31f;
            int n = (int)(Rate * L), fade = Rate * 2;
            var raw = new float[(n + fade) * 2];
            var rng = new System.Random(4242);
            var lowL = Biquad.LowPass(230f); var lowR = Biquad.LowPass(250f);
            var lapL = Biquad.BandPass(520f, 1.4f); var lapR = Biquad.BandPass(480f, 1.4f);
            var dripLp = Biquad.LowPass(2400f);
            // Laps: (time, length, pan), sparse; drips: (time, pitch, pan), 4-9 s apart.
            var laps = new List<(float t, float len, float pan)>();
            for (float t = 1.2f; t < L + 2f; t += 2.5f + (float)rng.NextDouble() * 4f)
                laps.Add((t, 0.6f + (float)rng.NextDouble() * 0.8f, (float)rng.NextDouble()));
            var drips = new List<(float t, float hz, float pan)>();
            for (float t = 2.5f; t < L + 2f; t += 4f + (float)rng.NextDouble() * 5f)
                drips.Add((t, 1100f + (float)rng.NextDouble() * 900f, (float)rng.NextDouble()));
            for (int i = 0; i < n + fade; i++)
            {
                float t = i / (float)Rate;
                float breathL = 0.75f + 0.25f * Mathf.Sin(TwoPi * 0.071f * t) + 0.1f * Mathf.Sin(TwoPi * 0.19f * t + 1.1f);
                float breathR = 0.75f + 0.25f * Mathf.Sin(TwoPi * 0.064f * t + 2.0f) + 0.1f * Mathf.Sin(TwoPi * 0.17f * t + 0.3f);
                float l = lowL.Run(White(rng)) * 1.1f * breathL, r = lowR.Run(White(rng)) * 1.1f * breathR;
                float wl = lapL.Run(White(rng)), wr = lapR.Run(White(rng));
                foreach (var (t0, len, pan) in laps)
                {
                    float u = (t - t0) / len;
                    if (u < 0f || u > 1f) continue;
                    float e = Mathf.Sin(u * Mathf.PI); e *= e * 0.22f;
                    l += wl * e * (1f - pan * 0.7f); r += wr * e * (0.3f + pan * 0.7f);
                }
                foreach (var (t0, hz, pan) in drips)
                {
                    float dt = t - t0;
                    if (dt < 0f || dt > 0.08f) continue;
                    // A drop: the faint tick of it landing, then the small bubble it leaves ringing a few ms (barely rising).
                    float tick = dt < 0.002f ? White(rng) * (1f - dt / 0.002f) * 0.15f : 0f;
                    float bub = Mathf.Sin(TwoPi * hz * (1f + dt * 2.5f) * dt) * Mathf.Exp(-dt * 70f) * 0.22f;
                    float d = dripLp.Run(tick + bub);
                    l += d * (1f - pan); r += d * pan;
                }
                raw[i * 2] = l; raw[i * 2 + 1] = r;
            }
            var data = Seamless(raw, n, fade, 2);
            Normalize(data, 0.4f);
            return Clip("PoolAmbience", data, 2);
        }

        /// <summary>Aerosol hiss (loop): broadband noise, brightest around 5 kHz, with a slight flutter.</summary>
        public static AudioClip SprayHiss()
        {
            float L = 2f;
            int n = (int)(Rate * L), fade = Rate / 4;
            var raw = new float[n + fade];
            var rng = new System.Random(515);
            var bp = Biquad.BandPass(5200f, 0.55f);
            var hp = Biquad.HighPass(1500f);
            for (int i = 0; i < n + fade; i++)
            {
                float t = i / (float)Rate;
                float flutter = 1f + 0.08f * Mathf.Sin(TwoPi * 23f * t) + 0.05f * Mathf.Sin(TwoPi * 37f * t + 1f);
                raw[i] = (bp.Run(White(rng)) * 0.9f + hp.Run(White(rng)) * 0.25f) * flutter;
            }
            var data = Seamless(raw, n, fade, 1);
            Normalize(data, 0.5f);
            return Clip("SprayHiss", data, 1);
        }

        /// <summary>The mixing ball rattling in a spray can (variant 0-3): a few quick metallic clicks.</summary>
        public static AudioClip CanRattle(int variant)
        {
            int n = (int)(Rate * 0.5f);
            var d = new float[n];
            var rng = new System.Random(808 + variant * 7);
            int hits = 4 + variant % 3;
            for (int h = 0; h < hits; h++)
            {
                float at = 0.02f + h * (0.07f + (float)rng.NextDouble() * 0.03f);
                float hz = 2600f + (float)rng.NextDouble() * 1400f;
                float amp = 0.5f + (float)rng.NextDouble() * 0.5f;
                int s0 = (int)(at * Rate);
                for (int i = s0; i < n; i++)
                {
                    float dt = (i - s0) / (float)Rate;
                    if (dt > 0.05f) break;
                    d[i] += (Mathf.Sin(TwoPi * hz * dt) * 0.7f + Mathf.Sin(TwoPi * hz * 2.7f * dt) * 0.3f + White(rng) * 0.3f) * Mathf.Exp(-dt * 140f) * amp;
                }
            }
            Normalize(d, 0.6f);
            return Clip("CanRattle_" + variant, d, 1);
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

        // ---------------- Camcorder ----------------

        /// <summary>Zoom lens motor: a thin, slightly wavering whine with gear ripple (seamless 2 s loop).</summary>
        public static AudioClip ZoomMotor()
        {
            float L = 2f;
            int n = (int)(Rate * L), fade = Rate / 10;
            var raw = new float[n + fade];
            var rng = new System.Random(808);
            var gearBp = Biquad.BandPass(3400f, 1.5f);
            var hissBp = Biquad.BandPass(6000f, 0.8f);
            float ph = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 1180f + 18f * Mathf.Sin(TwoPi * 3f * t) + 6f * Mathf.Sin(TwoPi * 11f * t);
                ph += TwoPi * f / Rate;
                float whine = Mathf.Sin(ph) * 0.5f + Mathf.Sin(ph * 2f) * 0.22f + Mathf.Sin(ph * 3f + 1f) * 0.1f;
                float ripple = 0.6f + 0.4f * Mathf.Sin(TwoPi * 47f * t);            // gear teeth
                float gear = gearBp.Run(White(rng)) * ripple * 1.5f;
                raw[i] = whine * 0.35f + gear * 0.5f + hissBp.Run(White(rng)) * 0.15f;
            }
            var data = Seamless(raw, n, fade, 1);
            Normalize(data, 0.5f);
            return Clip("ZoomMotor", data, 1);
        }

        // ---------------- Footsteps ----------------

        /// <summary>
        /// A step through shin-deep water (variant 0-7; 2026-10-31, rebuilt: the old one's droplets swept up four times in
        /// pitch and sounded like blasters). Most of the sound of water is small bubbles caught under it, each ringing a few
        /// milliseconds at a pitch set by its size (Minnaert: about 6.5 kHz over its diameter in mm) and barely rising; so:
        /// the low slosh of the water pushed aside, a soft swoosh of the leg going through, a little spray, a fizz of
        /// dozens of tiny bubbles crowded at the start and thinning out, and two or three drops falling back.
        /// </summary>
        public static AudioClip WaterStep(int variant)
        {
            int n = (int)(Rate * 0.75f);
            var d = new float[n];
            var rng = new System.Random(1700 + variant * 31);
            var sloshLp = Biquad.LowPass(520f + variant * 25f); var sloshBp = Biquad.BandPass(240f + variant * 15f, 0.8f);
            var swoosh = Biquad.BandPass(800f + (variant % 4) * 90f, 0.7f);
            var spray = Biquad.HighPass(3200f);
            var air = Biquad.LowPass(5200f);
            float wob1 = 8f + (float)rng.NextDouble() * 5f, wob2 = 13f + (float)rng.NextDouble() * 6f, ph = (float)rng.NextDouble() * 6f;
            float swooshLen = 0.22f + (float)rng.NextDouble() * 0.1f;
            // Bubbles: (start, pitch, ring time, level); most in the first tenth of a second.
            int count = 55 + rng.Next(30);
            var bubbles = new (float at, float hz, float tau, float amp)[count];
            for (int k = 0; k < count; k++)
            {
                float at = 0.015f - Mathf.Log(1f - (float)rng.NextDouble() * 0.98f) * 0.09f;
                float diam = 1.8f + (float)rng.NextDouble() * (float)rng.NextDouble() * 5.5f;      // mm, mostly small
                float hz = 6520f / diam;
                bubbles[k] = (at, hz, Mathf.Clamp(5.5f / hz * 1000f, 1.5f, 6f) / 1000f, (0.02f + (float)rng.NextDouble() * 0.06f) * Mathf.Sqrt(diam / 3f) * Mathf.Exp(-at * 4f));
            }
            int dropCount = 2 + rng.Next(2);
            var drops = new (float at, float hz)[dropCount];
            for (int k = 0; k < dropCount; k++) drops[k] = (0.22f + (float)rng.NextDouble() * 0.35f, 900f + (float)rng.NextDouble() * 700f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                // Slosh: the water pushed aside and settling back, wobbling.
                float se = Mathf.Min(1f, t / 0.03f) * Mathf.Exp(-t / 0.17f) * (1f + 0.3f * Mathf.Sin(TwoPi * wob1 * t + ph) * Mathf.Sin(TwoPi * wob2 * t));
                float w = White(rng);
                float slosh = (sloshLp.Run(w) * 0.8f + sloshBp.Run(w) * 1.2f) * se;
                // Swoosh: the leg moving through.
                float u = t / swooshLen;
                float swe = u < 1f ? Mathf.Sin(u * Mathf.PI) * (1f - u * 0.4f) : 0f;
                float sw = swoosh.Run(White(rng)) * swe * 0.35f;
                // Spray: a little hiss at the start.
                float sp = spray.Run(White(rng)) * Mathf.Min(1f, t / 0.008f) * Mathf.Exp(-t / 0.06f) * 0.12f;
                // Bubbles.
                float bub = 0f;
                foreach (var (at, hz, tau, amp) in bubbles)
                {
                    float dt = t - at;
                    if (dt < 0f || dt > tau * 5f) continue;
                    bub += Mathf.Sin(TwoPi * hz * (1f + 0.1f * hz * 0.001f * dt * 40f) * dt) * Mathf.Exp(-dt / tau) * amp;
                }
                // Drops falling back.
                float dr = 0f;
                foreach (var (at, hz) in drops)
                {
                    float dt = t - at;
                    if (dt < 0f || dt > 0.05f) continue;
                    dr += Mathf.Sin(TwoPi * hz * (1f + dt * 3f) * dt) * Mathf.Exp(-dt * 90f) * 0.07f + (dt < 0.0015f ? White(rng) * 0.05f : 0f);
                }
                d[i] = slosh + sw + sp + air.Run(bub + dr);
            }
            for (int i = 0; i < 600; i++) d[n - 1 - i] *= i / 600f;
            Normalize(d, 0.55f);
            return Clip("WaterStep_" + variant, d, 1);
        }

        /// <summary>
        /// Wading: the steady swish of legs moving through water, swelling with each stride, with a faint fizz of bubbles.
        /// 4 s seamless loop; its level follows the player's speed in water (PlayerAudio).
        /// </summary>
        public static AudioClip WadeLoop()
        {
            float L = 4f;
            int n = (int)(Rate * L), fade = Rate / 2;
            var raw = new float[n + fade];
            var rng = new System.Random(5150);
            var body = Biquad.BandPass(330f, 0.6f); var hiss = Biquad.BandPass(1300f, 0.8f); var fizz = Biquad.HighPass(2500f);
            for (int i = 0; i < n + fade; i++)
            {
                float t = i / (float)Rate;
                // Two legs: swells at the stride (about 1.6 a second), uneven.
                float sw = 0.35f + 0.65f * Mathf.Pow(Mathf.Abs(Mathf.Sin(TwoPi * 0.8f * t + 0.3f * Mathf.Sin(TwoPi * 0.25f * t))), 1.5f);
                float w = White(rng);
                raw[i] = (body.Run(w) * 1.0f + hiss.Run(White(rng)) * 0.35f) * sw + fizz.Run(White(rng)) * 0.04f * sw * sw;
            }
            var data = Seamless(raw, n, fade, 1);
            Normalize(data, 0.4f);
            return Clip("WadeLoop", data, 1);
        }

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
