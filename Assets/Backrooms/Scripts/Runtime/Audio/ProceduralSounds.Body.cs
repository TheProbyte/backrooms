using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// The operator's body and the building settling: footsteps on hard floors (tile, wood, concrete, the sheet metal of a
    /// duct), a shoe scuffing as you stop, clothes rustling, a shoulder brushing a wall, breathing, a palm on a wall, and
    /// the small creaks and ticks of a building at rest (ceiling tiles, furniture).
    /// </summary>
    public static partial class ProceduralSounds
    {
        public enum Floor { Carpet, Tile, Wood, Concrete, Metal, Water }

        /// <summary>A footstep on a hard floor: a heel strike and the sole rolling flat, coloured by what the floor is.</summary>
        public static AudioClip HardStep(Floor floor, int variant)
        {
            var rng = new System.Random(1300 + (int)floor * 31 + variant * 7);
            int n = Rate * 2 / 5;
            var d = new float[n];
            float heelHz, bodyHz, ring, rollGap, bright;
            switch (floor)
            {
                case Floor.Tile: heelHz = 2600f; bodyHz = 520f; ring = 0.018f; rollGap = 0.045f; bright = 1.2f; break;
                case Floor.Wood: heelHz = 1500f; bodyHz = 190f; ring = 0.03f; rollGap = 0.05f; bright = 0.7f; break;
                case Floor.Metal: heelHz = 1900f; bodyHz = 310f; ring = 0.09f; rollGap = 0.06f; bright = 0.9f; break;
                default: heelHz = 2100f; bodyHz = 260f; ring = 0.012f; rollGap = 0.05f; bright = 0.8f; break;   // concrete
            }
            float jitter = 1f + ((float)rng.NextDouble() - 0.5f) * 0.12f;
            var heel = Biquad.BandPass(heelHz * jitter, 1.4f); var grit = Biquad.HighPass(3500f);
            var body = Biquad.BandPass(bodyHz * jitter, floor == Floor.Metal ? 7f : 1.6f);
            var roll = Biquad.BandPass(heelHz * 0.6f * jitter, 1.1f);
            int rollAt = (int)(rollGap * Rate * (0.85f + 0.3f * (float)rng.NextDouble()));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float e1 = Mathf.Exp(-t / 0.007f);
                float tr = (i - rollAt) / (float)Rate;
                float e2 = tr >= 0f ? Mathf.Exp(-tr / 0.02f) * 0.55f : 0f;
                float er = Mathf.Exp(-t / ring);
                float x = White(rng);
                d[i] = heel.Run(x * e1) * 1.6f + grit.Run(x * e1) * 0.35f * bright + roll.Run(x * e2) * 0.9f + body.Run(x * er) * (floor == Floor.Metal ? 2.2f : 1.4f);
            }
            Normalize(d, 0.75f);
            return Clip("Step_" + floor + "_" + variant, d, 1);
        }

        /// <summary>A shoe sliding a few centimetres as you come to a stop (the carpet's fibres, or a hard floor's grit).</summary>
        public static AudioClip Scuff(int variant)
        {
            var rng = new System.Random(1400 + variant);
            int n = Rate / 4;
            var d = new float[n];
            var bp = Biquad.BandPass(1700f + variant * 300f, 0.8f); var low = Biquad.LowPass(900f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f)) * (0.7f + 0.3f * Mathf.PerlinNoise(t * 30f, variant));
                float x = White(rng);
                d[i] = (bp.Run(x) + low.Run(x) * 0.4f) * env;
            }
            Normalize(d, 0.4f);
            return Clip("Scuff_" + variant, d, 1);
        }

        /// <summary>Clothes moving: soft broadband fabric noise with little swishes. Loops; its level follows how much you move.</summary>
        public static AudioClip Rustle()
        {
            int fade = Rate / 8, n = Rate * 3 + fade;
            var raw = new float[n];
            var rng = new System.Random(1500);
            var bp = Biquad.BandPass(2400f, 0.6f); var hp = Biquad.HighPass(5200f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float swish = Mathf.Pow(Mathf.PerlinNoise(t * 3.1f, 0.4f), 2f) * 1.6f;
                float crinkle = rng.NextDouble() < 0.002 ? 1.5f : 0f;
                float x = White(rng);
                raw[i] = bp.Run(x) * swish + hp.Run(x + crinkle) * 0.25f * swish;
            }
            var d = LoopJoin(raw, fade);
            Normalize(d, 0.35f);
            return Clip("Rustle", d, 1);
        }

        /// <summary>A sleeve dragging along a wall: a rough, slightly tonal rub. Loops.</summary>
        public static AudioClip Brush()
        {
            int fade = Rate / 8, n = Rate * 2 + fade;
            var raw = new float[n];
            var rng = new System.Random(1600);
            var bp = Biquad.BandPass(1100f, 0.9f); var bp2 = Biquad.BandPass(3100f, 1.2f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float rough = 0.6f + 0.4f * Mathf.PerlinNoise(t * 17f, 2.2f);
                float x = White(rng);
                raw[i] = (bp.Run(x) * 0.9f + bp2.Run(x) * 0.5f) * rough;
            }
            var d = LoopJoin(raw, fade);
            Normalize(d, 0.4f);
            return Clip("Brush", d, 1);
        }

        /// <summary>
        /// A breath, close to the microphone, through a half-open mouth: air rushing through the throat and mouth, so soft
        /// noise shaped by the mouth's resonances (a "haaa" for out, a slightly brighter, quieter "hhh" for in) with a
        /// little flutter, and on the way out a low puff of air on the microphone. In: swells and stops; out: starts
        /// firmly and trails away. (Until 2026-10-27 it was white noise through high band-passes: a hiss.)
        /// </summary>
        public static AudioClip Breath(bool inhale, int variant)
        {
            var rng = new System.Random(1700 + (inhale ? 0 : 50) + variant);
            float len = (inhale ? 0.95f : 1.25f) * (0.88f + 0.24f * (float)rng.NextDouble());
            int n = (int)(Rate * len);
            var d = new float[n];
            // The mouth's resonances (formants), a little different each breath.
            float shift = 0.9f + 0.2f * (float)rng.NextDouble();
            var f1 = Biquad.BandPass((inhale ? 480f : 560f) * shift, 1.4f);
            var f2 = Biquad.BandPass((inhale ? 1650f : 1250f) * shift, 1.8f);
            var f3 = Biquad.BandPass((inhale ? 2900f : 2400f) * shift, 2.4f);
            var puff = Biquad.LowPass(160f);
            var soft = Biquad.LowPass(inhale ? 4200f : 3200f);
            float b0 = 0f, b1 = 0f, b2 = 0f;                                   // pink noise (Paul Kellet's filter)
            float seed = (float)rng.NextDouble() * 100f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float w = White(rng);
                b0 = 0.99765f * b0 + w * 0.0990460f; b1 = 0.96300f * b1 + w * 0.2965164f; b2 = 0.57000f * b2 + w * 1.0526913f;
                float pink = (b0 + b1 + b2 + w * 0.1848f) * 0.25f;
                // In: a slow swell, cut off at the top. Out: a firm start, a long fall.
                float env = inhale ? Mathf.Pow(Mathf.Sin(Mathf.PI * 0.5f * Mathf.Clamp01(t / 0.8f)), 1.5f) * Mathf.Clamp01((1f - t) / 0.12f)
                                   : Mathf.Clamp01(t / 0.1f) * Mathf.Pow(1f - t, 1.6f);
                float flutter = 0.85f + 0.15f * Mathf.PerlinNoise(t * len * 9f, seed);
                float air = inhale ? f1.Run(pink) * 0.55f + f2.Run(pink) * 0.75f + f3.Run(pink) * 0.35f
                                   : f1.Run(pink) * 1.0f + f2.Run(pink) * 0.55f + f3.Run(pink) * 0.18f;
                float mic = inhale ? 0f : puff.Run(w) * 2.2f * Mathf.Clamp01((0.6f - t) / 0.6f);      // air on the microphone
                d[i] = soft.Run(air + mic) * env * flutter;
            }
            Normalize(d, inhale ? 0.18f : 0.24f);
            return Clip("Breath_" + (inhale ? "In" : "Out") + variant, d, 1);
        }

        /// <summary>A palm put flat on a wall: a soft dull pat.</summary>
        public static AudioClip WallPat(int variant)
        {
            var d = new float[Rate / 5];
            var rng = new System.Random(1800 + variant);
            WoodKnock(d, 0, 0.6f, 330f + variant * 40f, rng);
            var hp = Biquad.HighPass(2500f);
            for (int i = 0; i < Rate / 60; i++) d[i] += hp.Run(White(rng)) * Mathf.Exp(-i / (Rate * 0.004f)) * 0.3f;
            Normalize(d, 0.35f);
            return Clip("WallPat_" + variant, d, 1);
        }

        /// <summary>A ceiling tile shifting in its grid: a dry crackle and a tiny tick of the metal tee.</summary>
        public static AudioClip CeilingCreak(int variant)
        {
            var rng = new System.Random(1900 + variant);
            int n = Rate * 3 / 5;
            var d = new float[n];
            var bp = Biquad.BandPass(2200f + variant * 400f, 1.5f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float burst = Mathf.PerlinNoise(t * 40f, variant * 3.3f);
                float pop = rng.NextDouble() < 0.004 * burst ? 1f : 0f;
                d[i] = bp.Run(pop * 2f + White(rng) * 0.05f * burst) * Mathf.Exp(-t / 0.25f);
            }
            MetalTick(d, (int)(Rate * (0.05f + 0.1f * variant / 3f)), 0.3f, 3800f, rng);
            Normalize(d, 0.5f);
            return Clip("CeilingCreak_" + variant, d, 1);
        }

        /// <summary>Furniture settling: a small knock of wood, a faint creak of a joint.</summary>
        public static AudioClip FurnitureTick(int variant)
        {
            var rng = new System.Random(2000 + variant);
            var d = new float[Rate / 3];
            WoodKnock(d, 0, 0.7f, 420f + variant * 90f, rng);
            var bp = Biquad.BandPass(900f, 5f);
            for (int i = Rate / 50; i < Rate / 6; i++)
            {
                float t = (i - Rate / 50) / (float)Rate;
                d[i] += bp.Run(rng.NextDouble() < 0.02 ? 0.8f : 0f) * Mathf.Exp(-t / 0.06f);
            }
            Normalize(d, 0.45f);
            return Clip("FurnitureTick_" + variant, d, 1);
        }
    }

    public static partial class SoundBank
    {
        static AudioClip[,] s_hardSteps;
        static AudioClip[] s_scuffs, s_breathIn, s_breathOut, s_pats, s_creaks, s_ticks;
        static AudioClip s_rustle, s_brush;

        /// <summary>A footstep on a hard floor (tile, wood, concrete, metal): 6 variants each.</summary>
        public static AudioClip HardStep(ProceduralSounds.Floor floor, int variant)
        {
            if (s_hardSteps == null) s_hardSteps = new AudioClip[6, 6];
            int f = Mathf.Clamp((int)floor, 0, 5), v = ((variant % 6) + 6) % 6;
            return s_hardSteps[f, v] ? s_hardSteps[f, v] : (s_hardSteps[f, v] = ProceduralSounds.HardStep(floor, v));
        }

        public static AudioClip Scuff(int i) => Pick(ref s_scuffs, 4, i, ProceduralSounds.Scuff);
        public static AudioClip BreathIn(int i) => Pick(ref s_breathIn, 3, i, v => ProceduralSounds.Breath(true, v));
        public static AudioClip BreathOut(int i) => Pick(ref s_breathOut, 3, i, v => ProceduralSounds.Breath(false, v));
        public static AudioClip WallPat(int i) => Pick(ref s_pats, 3, i, ProceduralSounds.WallPat);
        public static AudioClip CeilingCreak(int i) => Pick(ref s_creaks, 4, i, ProceduralSounds.CeilingCreak);
        public static AudioClip FurnitureTick(int i) => Pick(ref s_ticks, 4, i, ProceduralSounds.FurnitureTick);
        public static AudioClip Rustle => s_rustle ? s_rustle : (s_rustle = ProceduralSounds.Rustle());
        public static AudioClip Brush => s_brush ? s_brush : (s_brush = ProceduralSounds.Brush());

        static AudioClip Pick(ref AudioClip[] set, int count, int i, System.Func<int, AudioClip> make)
        {
            if (set == null) set = new AudioClip[count];
            int v = ((i % count) + count) % count;
            return set[v] ? set[v] : (set[v] = make(v));
        }
    }
}
