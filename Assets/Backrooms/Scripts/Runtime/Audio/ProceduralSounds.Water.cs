using UnityEngine;

namespace Backrooms.Audio
{
    public static partial class ProceduralSounds
    {
        /// <summary>
        /// Something going into the water (2026-10-31): size 0 small (a can, chalk), 1 middling (a hammer, a tape roll), 2 big
        /// (a box, a chair). The plop people know is the air cavity the object drags down pinching off: one bubble, low
        /// for a big cavity, rising a little as it closes; with the splash's noise (more for bigger things), a fizz of
        /// small bubbles and drops falling back.
        /// </summary>
        public static AudioClip Plop(int size, int variant)
        {
            var rng = new System.Random(3100 + size * 97 + variant * 13);
            float len = size == 0 ? 0.45f : size == 1 ? 0.7f : 1.1f;
            int n = (int)(Rate * len);
            var d = new float[n];
            float f0 = size == 0 ? 520f + (float)rng.NextDouble() * 260f : size == 1 ? 300f + (float)rng.NextDouble() * 140f : 170f + (float)rng.NextDouble() * 70f;
            float cavityAt = size == 0 ? 0.012f : size == 1 ? 0.03f : 0.06f, cavityTau = size == 0 ? 0.035f : size == 1 ? 0.05f : 0.08f;
            float splashAmt = size == 0 ? 0.25f : size == 1 ? 0.6f : 1.1f;
            var splashBp = Biquad.BandPass(size == 2 ? 700f : 1300f, 0.7f);
            var sloshLp = Biquad.LowPass(size == 2 ? 450f : 700f);
            var air = Biquad.LowPass(6000f);
            int count = size == 0 ? 15 : size == 1 ? 35 : 70;
            var bubbles = new (float at, float hz, float tau, float amp)[count];
            for (int k = 0; k < count; k++)
            {
                float at = cavityAt - Mathf.Log(1f - (float)rng.NextDouble() * 0.98f) * (0.06f + size * 0.05f);
                float diam = 1.8f + (float)rng.NextDouble() * (float)rng.NextDouble() * 6f;
                float hz = 6520f / diam;
                bubbles[k] = (at, hz, Mathf.Clamp(5.5f / hz, 0.0015f, 0.006f), (0.02f + (float)rng.NextDouble() * 0.05f) * Mathf.Exp(-(at - cavityAt) * 5f));
            }
            int drops = 1 + size + rng.Next(2);
            var falls = new (float at, float hz)[drops];
            for (int k = 0; k < drops; k++) falls[k] = (0.15f + (float)rng.NextDouble() * (len - 0.3f), 800f + (float)rng.NextDouble() * 900f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                // The hit: a short burst of splash noise, and the water heaved aside.
                float hit = splashBp.Run(White(rng)) * Mathf.Min(1f, t / 0.003f) * Mathf.Exp(-t / (0.04f + size * 0.05f)) * splashAmt;
                float slosh = sloshLp.Run(White(rng)) * Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t / (0.1f + size * 0.12f)) * (0.3f + size * 0.4f);
                // The cavity pinching off: one low bubble rising half an octave as it closes.
                float ct = t - cavityAt;
                float cav = ct > 0f ? Mathf.Sin(6.2831853f * f0 * (1f + 0.6f * ct / cavityTau) * ct) * Mathf.Exp(-ct / cavityTau) * Mathf.Min(1f, ct / 0.002f) * 0.55f : 0f;
                float bub = 0f;
                foreach (var (at, hz, tau, amp) in bubbles)
                {
                    float dt = t - at;
                    if (dt < 0f || dt > tau * 5f) continue;
                    bub += Mathf.Sin(6.2831853f * hz * (1f + 4f * dt) * dt) * Mathf.Exp(-dt / tau) * amp;
                }
                float dr = 0f;
                foreach (var (at, hz) in falls)
                {
                    float dt = t - at;
                    if (dt < 0f || dt > 0.05f) continue;
                    dr += Mathf.Sin(6.2831853f * hz * (1f + dt * 3f) * dt) * Mathf.Exp(-dt * 90f) * 0.08f;
                }
                d[i] = hit + slosh + cav + air.Run(bub + dr);
            }
            for (int i = 0; i < 500; i++) d[n - 1 - i] *= i / 500f;
            Normalize(d, 0.6f);
            return Clip("Plop_" + size + "_" + variant, d, 1);
        }

        /// <summary>A drop of water off you onto the tiles: the faint tick of it landing and a tiny splash.</summary>
        public static AudioClip Drip(int variant)
        {
            var rng = new System.Random(4400 + variant * 7);
            int n = (int)(Rate * 0.12f);
            var d = new float[n];
            var hp = Biquad.HighPass(1800f);
            float hz = 2200f + (float)rng.NextDouble() * 1800f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float tick = hp.Run(White(rng)) * Mathf.Exp(-t / 0.004f) * 0.8f;
                float ring = Mathf.Sin(6.2831853f * hz * t) * Mathf.Exp(-t / 0.006f) * 0.25f;
                d[i] = tick + ring;
            }
            Normalize(d, 0.5f);
            return Clip("Drip_" + variant, d, 1);
        }
    }
}
