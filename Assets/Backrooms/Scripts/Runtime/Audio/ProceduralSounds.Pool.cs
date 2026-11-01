using UnityEngine;

namespace Backrooms.Audio
{
    public static partial class ProceduralSounds
    {
        /// <summary>
        /// A drop falling from a roof opening into the pool (2026-11-01, the realism plan 8): the tick of it striking the
        /// surface and the bubble it traps ringing (Minnaert: about 6.5 kHz over its diameter in mm), rising a little as it
        /// closes; now and then a second, smaller one.
        /// </summary>
        public static AudioClip Plink(int variant)
        {
            var rng = new System.Random(5200 + variant * 11);
            int n = (int)(Rate * 0.18f);
            var d = new float[n];
            var hp = Biquad.HighPass(1800f);
            var soft = Biquad.LowPass(2600f);
            float hz = 650f + (float)rng.NextDouble() * 550f, hz2 = hz * (1.5f + (float)rng.NextDouble() * 0.4f);
            float at2 = rng.NextDouble() < 0.5 ? 0.03f + (float)rng.NextDouble() * 0.04f : 99f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float tick = hp.Run(White(rng)) * Mathf.Exp(-t / 0.002f) * 0.18f;
                float bt = t - 0.002f;
                float bub = bt > 0f ? Mathf.Sin(6.2831853f * hz * (1f + 1.2f * bt) * bt) * Mathf.Exp(-bt / 0.014f) * Mathf.Min(1f, bt / 0.0015f) : 0f;
                float b2t = t - at2;
                float bub2 = b2t > 0f ? Mathf.Sin(6.2831853f * hz2 * (1f + 1.5f * b2t) * b2t) * Mathf.Exp(-b2t / 0.008f) * 0.25f : 0f;
                d[i] = soft.Run(tick + bub + bub2);
            }
            Normalize(d, 0.5f);
            return Clip("Plink_" + variant, d, 1);
        }

        /// <summary>
        /// A ripple reaching a pool wall: the soft slap and suck of water against tile, a swell of low noise with a few small
        /// gurgles as it draws back.
        /// </summary>
        public static AudioClip Lap(int variant)
        {
            var rng = new System.Random(5300 + variant * 17);
            float len = 0.55f + (float)rng.NextDouble() * 0.25f;
            int n = (int)(Rate * len);
            var d = new float[n];
            var lp = Biquad.LowPass(900f);
            var bp = Biquad.BandPass(420f, 0.8f);
            float slapAt = 0.04f + (float)rng.NextDouble() * 0.04f;
            var bubbles = new (float at, float hz, float amp)[6];
            for (int k = 0; k < bubbles.Length; k++) bubbles[k] = (0.12f + (float)rng.NextDouble() * (len - 0.25f), 500f + (float)rng.NextDouble() * 900f, 0.04f + (float)rng.NextDouble() * 0.05f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float swell = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / len)) ;
                float wash = lp.Run(White(rng)) * swell * swell * 0.6f;
                float st = t - slapAt;
                float slap = st > 0f ? bp.Run(White(rng)) * Mathf.Exp(-st / 0.03f) * 0.7f : 0f;
                float g = 0f;
                foreach (var (at, hz, amp) in bubbles)
                {
                    float dt = t - at;
                    if (dt < 0f || dt > 0.04f) continue;
                    g += Mathf.Sin(6.2831853f * hz * (1f + 3f * dt) * dt) * Mathf.Exp(-dt / 0.008f) * amp;
                }
                d[i] = wash + slap + g;
            }
            for (int i = 0; i < 800; i++) d[n - 1 - i] *= i / 800f;
            Normalize(d, 0.5f);
            return Clip("Lap_" + variant, d, 1);
        }

    }
}
