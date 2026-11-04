using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// The sledgehammer and the walls it breaks: the swing through the air, a blow into drywall (a dull thump and the
    /// crunch of gypsum), board crumbling and pattering down, a galvanised steel stud's tinny bang and its tearing, a
    /// wood stud's solid thock and its splintering crack, a dead thud into something that does not give, a blow on a
    /// box or pipe, a batt's soft whump, and bits landing.
    /// </summary>
    public static partial class ProceduralSounds
    {
        /// <summary>Gypsum crunching: a burst of tiny cracks in the 1-5 kHz band, densest at the start.</summary>
        static void Crunch(float[] d, int at, float seconds, float gain, float density, System.Random rng, float hz = 2600f)
        {
            int len = (int)(Rate * seconds);
            var bp = Biquad.BandPass(hz, 0.7f); var bp2 = Biquad.BandPass(hz * 1.9f, 1.1f);
            float grain = 0f;
            for (int i = 0; i < len && at + i < d.Length; i++)
            {
                float t = i / (float)Rate, env = Mathf.Exp(-t / (seconds * 0.35f));
                if (rng.NextDouble() < density * env * 0.02) grain = 0.4f + (float)rng.NextDouble() * 0.9f;
                grain *= 0.86f;
                float x = White(rng) * grain;
                d[at + i] += (bp.Run(x) * 1.3f + bp2.Run(x) * 0.6f) * gain;
            }
        }

        /// <summary>A heavy dull thump (a body of air and board pushed).</summary>
        static void Thump(float[] d, int at, float gain, float hz, float decay, System.Random rng)
        {
            var lp = Biquad.LowPass(hz * 3f);
            int len = (int)(Rate * decay * 6f);
            for (int i = 0; i < len && at + i < d.Length; i++)
            {
                float t = i / (float)Rate, e = Mathf.Exp(-t / decay);
                float f = hz * (1f + 0.6f * Mathf.Exp(-t / 0.01f));              // pitch drops as the board gives
                d[at + i] += (Mathf.Sin(TwoPi * f * t) * 0.9f + lp.Run(White(rng)) * 0.6f) * e * gain;
            }
        }

        /// <summary>Sheet metal struck: inharmonic partials that die fast, with a buzz as the flanges rattle.</summary>
        static void SheetMetal(float[] d, int at, float gain, float scale, float decay, System.Random rng)
        {
            float[] f = { 310f, 740f, 1290f, 2110f, 3380f, 4720f };
            float[] a = { 0.8f, 1f, 0.7f, 0.55f, 0.35f, 0.2f };
            int len = (int)(Rate * decay * 5f);
            var ph = new float[f.Length];
            for (int k = 0; k < f.Length; k++) ph[k] = (float)rng.NextDouble() * TwoPi;
            for (int i = 0; i < len && at + i < d.Length; i++)
            {
                float t = i / (float)Rate, s = 0f;
                for (int k = 0; k < f.Length; k++) s += a[k] * Mathf.Sin(TwoPi * f[k] * scale * t + ph[k]) * Mathf.Exp(-t / (decay * (1.2f - k * 0.15f)));
                float rattle = Mathf.Clamp(Mathf.Sin(TwoPi * 95f * t) * 6f, -1f, 1f) * White(rng) * 0.25f * Mathf.Exp(-t / (decay * 0.8f));
                d[at + i] += (s * 0.45f + rattle) * gain;
            }
        }

        public static AudioClip HammerSwing(int variant)
        {
            float L = 0.42f + variant * 0.05f;
            int n = (int)(Rate * L);
            var d = new float[n];
            var rng = new System.Random(900 + variant);
            float lo = 0f, hi = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, x = t / L;
                // Air past the head: rises as it speeds up, gone at the end of the stroke; its pitch rises with the speed.
                float env = Mathf.Pow(Mathf.Sin(Mathf.Clamp01(x) * Mathf.PI), 1.6f) * (0.4f + 0.6f * x);
                float fc = 380f + 1100f * Mathf.Sin(x * Mathf.PI);
                float a = 1f - Mathf.Exp(-TwoPi * fc / Rate), b = 1f - Mathf.Exp(-TwoPi * fc * 0.35f / Rate);
                float w = White(rng);
                hi += a * (w - hi); lo += b * (w - lo);
                d[i] = (hi - lo) * env;
            }
            Normalize(d, 0.4f);
            return Clip("HammerSwing_" + variant, d, 1);
        }

        public static AudioClip DrywallHit(int variant)
        {
            var d = new float[(int)(Rate * 0.7f)];
            var rng = new System.Random(910 + variant);
            Thump(d, 0, 1f, 85f + variant * 12f, 0.05f, rng);
            Crunch(d, Rate / 400, 0.32f, 0.9f, 1.5f, rng, 2300f + variant * 300f);
            WoodKnock(d, 0, 0.25f, 330f, rng);
            Normalize(d, 0.85f);
            return Clip("DrywallHit_" + variant, d, 1);
        }

        /// <summary>Board breaking up and falling: crunch, then bits pattering down for most of a second.</summary>
        public static AudioClip DrywallCrumble(int variant)
        {
            var d = new float[(int)(Rate * 1.3f)];
            var rng = new System.Random(920 + variant);
            Crunch(d, 0, 0.5f, 0.8f, 2.2f, rng, 2800f);
            // Bits landing: little ticks on carpet and on each other.
            for (int k = 0; k < 18; k++)
            {
                int at = (int)(Rate * (0.18f + Mathf.Pow((float)rng.NextDouble(), 1.6f) * 0.95f));
                Crunch(d, at, 0.03f + (float)rng.NextDouble() * 0.03f, 0.25f + (float)rng.NextDouble() * 0.35f, 3f, rng, 1600f + (float)rng.NextDouble() * 2400f);
            }
            Normalize(d, 0.6f);
            return Clip("DrywallCrumble_" + variant, d, 1);
        }

        public static AudioClip SteelStudHit(int variant)
        {
            var d = new float[(int)(Rate * 0.9f)];
            var rng = new System.Random(930 + variant);
            SheetMetal(d, 0, 1f, 1f + variant * 0.07f, 0.09f, rng);
            Thump(d, 0, 0.5f, 120f, 0.03f, rng);
            Normalize(d, 0.75f);
            return Clip("SteelStudHit_" + variant, d, 1);
        }

        /// <summary>A steel stud folding and tearing: metal grinding and a crack, falling in pitch.</summary>
        public static AudioClip SteelTear()
        {
            int n = (int)(Rate * 0.55f);
            var d = new float[n];
            var rng = new System.Random(935);
            var high = Biquad.BandPass(2600f, 4f); var low = Biquad.BandPass(800f, 4f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float fall = Mathf.Clamp01(t / 0.35f);                         // the grind drops in pitch as it folds
                float x = White(rng);
                float crackle = rng.NextDouble() < 0.06 * Mathf.Exp(-t * 4f) ? 1f : 0f;
                d[i] = ((high.Run(x) * (1f - fall) + low.Run(x) * fall) * 0.9f + crackle * 0.8f) * Mathf.Exp(-t * 5f);
            }
            SheetMetal(d, 0, 0.6f, 0.85f, 0.07f, rng);
            Normalize(d, 0.6f);
            return Clip("SteelTear", d, 1);
        }

        public static AudioClip WoodStudHit(int variant)
        {
            var d = new float[(int)(Rate * 0.5f)];
            var rng = new System.Random(940 + variant);
            WoodKnock(d, 0, 1f, 210f + variant * 25f, rng);
            WoodKnock(d, Rate / 300, 0.5f, 520f, rng);
            Thump(d, 0, 0.35f, 95f, 0.03f, rng);
            Normalize(d, 0.8f);
            return Clip("WoodStudHit_" + variant, d, 1);
        }

        /// <summary>Wood splitting: a sharp crack and fibres tearing.</summary>
        public static AudioClip WoodCrack()
        {
            var d = new float[(int)(Rate * 0.6f)];
            var rng = new System.Random(945);
            var hp = Biquad.HighPass(900f);
            for (int i = 0; i < Rate / 60; i++) d[i] += hp.Run(White(rng)) * Mathf.Exp(-i / (Rate * 0.002f)) * 1.4f;
            for (int k = 0; k < 14; k++)
            {
                int at = (int)(Rate * (0.01f + (float)rng.NextDouble() * 0.28f));
                MetalTick(d, at, 0.2f + (float)rng.NextDouble() * 0.3f, 900f + (float)rng.NextDouble() * 1500f, rng);
            }
            WoodKnock(d, 0, 0.6f, 260f, rng);
            Normalize(d, 0.75f);
            return Clip("WoodCrack", d, 1);
        }

        /// <summary>Into something that does not give (a post, a pillar, the floor): a dead thud and the hammer head's ring.</summary>
        public static AudioClip HardThud(int variant)
        {
            var d = new float[(int)(Rate * 0.8f)];
            var rng = new System.Random(950 + variant);
            Thump(d, 0, 1f, 70f + variant * 10f, 0.035f, rng);
            WoodKnock(d, 0, 0.35f, 400f, rng);
            // The steel head rings faintly.
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] += (Mathf.Sin(TwoPi * 3150f * t) * 0.05f + Mathf.Sin(TwoPi * 4870f * t) * 0.03f) * Mathf.Exp(-t / 0.18f);
            }
            Normalize(d, 0.8f);
            return Clip("HardThud_" + variant, d, 1);
        }

        public static AudioClip MetalClank(int variant)
        {
            var d = new float[(int)(Rate * 0.7f)];
            var rng = new System.Random(960 + variant);
            SheetMetal(d, 0, 0.8f, 1.6f + variant * 0.2f, 0.06f, rng);
            MetalTick(d, 0, 0.8f, 2900f, rng);
            Normalize(d, 0.7f);
            return Clip("MetalClank_" + variant, d, 1);
        }

        public static AudioClip BattWhump()
        {
            var d = new float[(int)(Rate * 0.4f)];
            var rng = new System.Random(970);
            var lp = Biquad.LowPass(700f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = lp.Run(White(rng)) * Mathf.Exp(-t / 0.05f) + Mathf.Sin(TwoPi * 75f * t) * Mathf.Exp(-t / 0.03f) * 0.4f;
            }
            Normalize(d, 0.5f);
            return Clip("BattWhump", d, 1);
        }

        /// <summary>A bit of wall landing: board (a short crunchy tick), steel (a tinny clatter), wood (a clunk).</summary>
        public static AudioClip DebrisLand(int kind, int variant)
        {
            var d = new float[(int)(Rate * 0.3f)];
            var rng = new System.Random(980 + kind * 10 + variant);
            if (kind == 1) SheetMetal(d, 0, 0.5f, 1.3f + variant * 0.15f, 0.03f, rng);
            else if (kind == 2) WoodKnock(d, 0, 0.8f, 300f + variant * 60f, rng);
            else { Crunch(d, 0, 0.06f, 0.8f, 4f, rng, 1900f + variant * 500f); Thump(d, 0, 0.25f, 160f, 0.01f, rng); }
            Normalize(d, 0.5f);
            return Clip("DebrisLand_" + kind + "_" + variant, d, 1);
        }
    }

    public static partial class SoundBank
    {
        static AudioClip[] s_swing, s_dryHit, s_dryCrumble, s_steelHit, s_woodHit, s_hardThud, s_clank, s_land;
        static AudioClip s_steelTear, s_woodCrack, s_batt;

        static AudioClip PickClip(ref AudioClip[] set, int count, int i, System.Func<int, AudioClip> make)
        {
            if (set == null) set = new AudioClip[count];
            int k = ((i % count) + count) % count;
            if (!set[k]) set[k] = make(k);
            return set[k];
        }

        public static AudioClip HammerSwing(int i) => PickClip(ref s_swing, 3, i, ProceduralSounds.HammerSwing);
        public static AudioClip DrywallHit(int i) => PickClip(ref s_dryHit, 3, i, ProceduralSounds.DrywallHit);
        public static AudioClip DrywallCrumble(int i) => PickClip(ref s_dryCrumble, 2, i, ProceduralSounds.DrywallCrumble);
        public static AudioClip SteelStudHit(int i) => PickClip(ref s_steelHit, 3, i, ProceduralSounds.SteelStudHit);
        public static AudioClip WoodStudHit(int i) => PickClip(ref s_woodHit, 3, i, ProceduralSounds.WoodStudHit);
        public static AudioClip HardThud(int i) => PickClip(ref s_hardThud, 3, i, ProceduralSounds.HardThud);
        public static AudioClip MetalClank(int i) => PickClip(ref s_clank, 2, i, ProceduralSounds.MetalClank);
        public static AudioClip DebrisLand(int kind, int i) => PickClip(ref s_land, 9, kind * 3 + (((i % 3) + 3) % 3), k => ProceduralSounds.DebrisLand(k / 3, k % 3));
        public static AudioClip SteelTear => s_steelTear ? s_steelTear : (s_steelTear = ProceduralSounds.SteelTear());
        public static AudioClip WoodCrack => s_woodCrack ? s_woodCrack : (s_woodCrack = ProceduralSounds.WoodCrack());
        public static AudioClip BattWhump => s_batt ? s_batt : (s_batt = ProceduralSounds.BattWhump());
    }
}
