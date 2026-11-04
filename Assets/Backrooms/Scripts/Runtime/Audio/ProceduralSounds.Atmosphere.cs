using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>Sounds the tension director (Atmosphere/TensionDirector) places in the building.</summary>
    public static partial class ProceduralSounds
    {
        /// <summary>A knock from inside a wall: knuckles or something hard on gypsum board, hollow and dull (one blow).</summary>
        public static AudioClip WallKnock(int variant)
        {
            var d = new float[(int)(Rate * 0.5f)];
            var rng = new System.Random(2300 + variant);
            Contact(d, 0, 0.3f, 1300f + 200f * variant, 0.0025f, rng);
            Thump(d, 0, 0.8f, 135f + 18f * variant, 0.045f, rng);
            Modes(d, 0, 0.3f, new[] { 410f, 690f }, new[] { 1f, 0.5f }, new[] { 0.03f, 0.02f }, 1f + 0.05f * variant, rng);
            var lp = Biquad.LowPass(1400f);
            for (int i = 0; i < d.Length; i++) d[i] = lp.Run(d[i]);
            Normalize(d, 0.8f);
            return Clip("WallKnock_" + variant, Trim(d), 1);
        }

        /// <summary>Something shifting in a duct above the ceiling: a few soft sheet-metal thumps, irregular, fading off.</summary>
        public static AudioClip DuctThumps(int variant)
        {
            var d = new float[(int)(Rate * 3.2f)];
            var rng = new System.Random(2400 + variant);
            float t = 0.02f;
            int n = 4 + variant * 2;
            for (int k = 0; k < n && t < 2.6f; k++)
            {
                int at = (int)(Rate * t);
                float g = 0.9f * Mathf.Pow(0.85f, k) * (0.6f + 0.4f * (float)rng.NextDouble());
                SheetMetal(d, at, g * 0.5f, 0.55f + 0.1f * (float)rng.NextDouble(), 0.09f, rng);
                Thump(d, at, g * 0.6f, 85f, 0.03f, rng);
                t += 0.18f + (float)rng.NextDouble() * (k % 3 == 2 ? 0.7f : 0.3f);
            }
            var lp = Biquad.LowPass(2200f);
            for (int i = 0; i < d.Length; i++) d[i] = lp.Run(d[i]);
            Normalize(d, 0.7f);
            return Clip("DuctThumps_" + variant, Trim(d), 1);
        }
    }
}
