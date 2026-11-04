using UnityEngine;

namespace Backrooms.Audio
{
    public static partial class ProceduralSounds
    {
        /// <summary>
        /// Water falling into water (loop; the water park's mushrooms and arches, 2026-11-01): a soft broadband patter (the
        /// many drops of a broken sheet striking the surface, each a tiny tick) over a low churn, with the bubbles the drops
        /// trap ringing faintly (Minnaert, 1-4 kHz for millimetre bubbles). Kept low and smooth: it was the filtration hum's
        /// lesson that a loud constant noise in reach soon grates (2026-11-01).
        /// </summary>
        public static AudioClip WaterCurtain(int variant)
        {
            float L = 9f + variant * 2f;
            int n = (int)(Rate * L), fade = Rate;
            var raw = new float[n + fade];
            var rng = new System.Random(6100 + variant * 31);
            var patter = Biquad.BandPass(2600f, 0.55f);
            var air = Biquad.HighPass(5000f);
            var churn = Biquad.LowPass(380f);
            var soft = Biquad.LowPass(7000f);
            // Bubbles: random onsets, about 120 a second, each a few ms of a rising sine.
            int nb = (int)(L * 120f) + 40;
            var bubT = new float[nb]; var bubHz = new float[nb]; var bubA = new float[nb];
            for (int k = 0; k < nb; k++)
            {
                bubT[k] = (float)rng.NextDouble() * (L + 1f);
                bubHz[k] = 1000f + (float)rng.NextDouble() * 2800f;
                bubA[k] = 0.02f + (float)rng.NextDouble() * 0.05f;
            }
            System.Array.Sort(bubT, bubHz);
            int first = 0;
            for (int i = 0; i < n + fade; i++)
            {
                float t = i / (float)Rate;
                float flow = 0.85f + 0.1f * Mathf.Sin(TwoPi * 0.13f * t) + 0.05f * Mathf.Sin(TwoPi * 0.41f * t + 1.3f);
                float v = patter.Run(White(rng)) * 0.55f * flow + air.Run(White(rng)) * 0.08f + churn.Run(White(rng)) * 0.7f;
                while (first < nb && bubT[first] < t - 0.03f) first++;
                for (int k = first; k < nb && bubT[k] <= t; k++)
                {
                    float dt = t - bubT[k];
                    v += Mathf.Sin(TwoPi * bubHz[k] * (1f + 2f * dt) * dt) * Mathf.Exp(-dt / 0.005f) * bubA[k];
                }
                raw[i] = soft.Run(v);
            }
            var data = Seamless(raw, n, fade, 1);
            Normalize(data, 0.45f);
            return Clip("WaterCurtain_" + variant, data, 1);
        }

        /// <summary>
        /// A wave pool's wave (one shot, about 4 s): the deep thump and rush of the chamber letting its water go, then the
        /// swell running away down the pool, a long low wash that fades.
        /// </summary>
        public static AudioClip WaveSurge(int variant)
        {
            var rng = new System.Random(6200 + variant * 13);
            float len = 4.2f;
            int n = (int)(Rate * len);
            var d = new float[n];
            var thump = Biquad.LowPass(90f);
            var rush = Biquad.BandPass(700f, 0.5f);
            var wash = Biquad.LowPass(500f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float th = thump.Run(White(rng)) * Mathf.Exp(-t / 0.35f) * Mathf.Min(1f, t / 0.05f) * 3f;
                float ru = rush.Run(White(rng)) * Mathf.Exp(-t / 0.6f) * Mathf.Min(1f, t / 0.12f) * 0.5f;
                float sw = Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.2f) / (len - 0.2f)));
                float wa = wash.Run(White(rng)) * sw * sw * 0.55f;
                d[i] = th + ru + wa;
            }
            for (int i = 0; i < 2000; i++) d[n - 1 - i] *= i / 2000f;
            Normalize(d, 0.55f);
            return Clip("WaveSurge_" + variant, d, 1);
        }
    }
}
