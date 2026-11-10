using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>Sounds of the items (chalk, tape, lamp, cords, rubber chicken) and of noclip doors.</summary>
    public static partial class ProceduralSounds
    {
        /// <summary>Joins a clip's end onto its start over the last `fade` samples, so it loops without a click.</summary>
        static float[] LoopJoin(float[] raw, int fade)
        {
            int n = raw.Length - fade;
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = raw[i];
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                d[i] = raw[i] * t + raw[n + i] * (1f - t);
            }
            return d;
        }

        /// <summary>Chalk on a wall: stick-slip chatter (irregular gritty clicks) over a dry hiss. Loops.</summary>
        public static AudioClip ChalkScratch()
        {
            int fade = Rate / 10, n = Rate * 2 + fade;
            var raw = new float[n];
            var rng = new System.Random(911);
            var hi = Biquad.BandPass(5200f, 0.9f); var mid = Biquad.BandPass(2600f, 1.4f); var hiss = Biquad.HighPass(3000f);
            float next = 0f, env = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                if (t >= next) { env = 0.6f + (float)rng.NextDouble() * 0.6f; next = t + 1f / (160f + (float)rng.NextDouble() * 220f); }
                env *= 0.9965f;
                float x = White(rng);
                raw[i] = hi.Run(x) * env * 1.4f + mid.Run(x) * env * 0.8f + hiss.Run(x) * 0.12f;
            }
            var d = LoopJoin(raw, fade);
            Normalize(d, 0.5f);
            return Clip("ChalkScratch", d, 1);
        }

        /// <summary>Tape peeling off its roll: dense crackle, louder in little waves. Loops.</summary>
        public static AudioClip TapePull()
        {
            int fade = Rate / 10, n = Rate * 3 / 2 + fade;
            var raw = new float[n];
            var rng = new System.Random(733);
            var bp = Biquad.BandPass(3400f, 0.7f); var low = Biquad.BandPass(900f, 1.1f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float click = rng.NextDouble() < 0.06 ? White(rng) * 3f : 0f;
                float waves = 0.55f + 0.45f * Mathf.Sin(t * 2f * Mathf.PI * 7f + Mathf.Sin(t * 13f) * 2f);
                raw[i] = (bp.Run(click + White(rng) * 0.25f) + low.Run(click) * 0.4f) * waves;
            }
            var d = LoopJoin(raw, fade);
            Normalize(d, 0.45f);
            return Clip("TapePull", d, 1);
        }

        /// <summary>A strip of tape torn off: a short rip that drops in pitch, then a pat as it is pressed down.</summary>
        public static AudioClip TapeTear()
        {
            int n = (int)(Rate * 0.3f);
            var d = new float[n];
            var rng = new System.Random(377);
            var pat = Biquad.LowPass(500f);
            var hiRip = Biquad.BandPass(5200f, 1.1f); var loRip = Biquad.BandPass(1800f, 1.1f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = Mathf.Clamp01(t / 0.12f);
                float x = White(rng) * (rng.NextDouble() < 0.3 ? 2.5f : 0.6f);
                float rip = t < 0.13f ? (hiRip.Run(x) * (1f - s) + loRip.Run(x) * s) * Mathf.Min(1f, t / 0.005f) : 0f;
                float thump = t > 0.18f ? pat.Run(White(rng)) * Mathf.Exp(-(t - 0.18f) * 60f) * 2f : 0f;
                d[i] = rip + thump;
            }
            Normalize(d, 0.55f);
            return Clip("TapeTear", d, 1);
        }

        /// <summary>A push-button switch: two little plastic clicks.</summary>
        public static AudioClip SwitchClick()
        {
            int n = (int)(Rate * 0.06f);
            var d = new float[n];
            var rng = new System.Random(55);
            var bp = Biquad.BandPass(3800f, 2f); var bp2 = Biquad.BandPass(2200f, 2f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                d[i] = bp.Run(White(rng)) * Mathf.Exp(-t * 700f) * 2f + (t > 0.022f ? bp2.Run(White(rng)) * Mathf.Exp(-(t - 0.022f) * 500f) : 0f);
            }
            Normalize(d, 0.5f);
            return Clip("SwitchClick", d, 1);
        }

        /// <summary>A plug pushed into a socket (scrape, then a solid seat), or pulled out (a pop).</summary>
        public static AudioClip Plug(bool into)
        {
            int n = (int)(Rate * (into ? 0.16f : 0.12f));
            var d = new float[n];
            var rng = new System.Random(into ? 81 : 82);
            var scrape = Biquad.BandPass(1700f, 1.5f); var thump = Biquad.LowPass(220f); var click = Biquad.BandPass(3000f, 2f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = White(rng);
                if (into)
                    d[i] = (t < 0.07f ? scrape.Run(x) * 0.5f * Mathf.Sin(t / 0.07f * Mathf.PI) : 0f)
                         + (t > 0.07f ? thump.Run(x) * Mathf.Exp(-(t - 0.07f) * 70f) * 4f + click.Run(x) * Mathf.Exp(-(t - 0.07f) * 400f) : 0f);
                else
                    d[i] = click.Run(x) * Mathf.Exp(-t * 300f) * 1.5f + thump.Run(x) * Mathf.Exp(-t * 50f) * 3f;
            }
            Normalize(d, 0.6f);
            return Clip(into ? "PlugIn" : "PlugOut", d, 1);
        }

        /// <summary>A plug yanked out by its cord: a hard pop, the cord snapping taut, prongs scraping.</summary>
        public static AudioClip CordYank()
        {
            int n = (int)(Rate * 0.32f);
            var d = new float[n];
            var rng = new System.Random(5151);
            var pop = Biquad.LowPass(300f); var snap = Biquad.BandPass(1800f, 1.2f); var scrape = Biquad.BandPass(3600f, 2f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, x = White(rng);
                d[i] = pop.Run(x) * Mathf.Exp(-t * 35f) * 5f
                     + snap.Run(x) * Mathf.Exp(-t * 60f) * 2.2f
                     + (t < 0.05f ? scrape.Run(x) * 0.8f : 0f)
                     + Mathf.Sin(t * 2f * Mathf.PI * 95f) * Mathf.Exp(-t * 18f) * 0.5f;
            }
            Normalize(d, 0.8f);
            return Clip("CordYank", d, 1);
        }

        /// <summary>A cord whipping back into a coil: a swish and a slap.</summary>
        public static AudioClip CordWhip()
        {
            int n = (int)(Rate * 0.45f);
            var d = new float[n];
            var rng = new System.Random(4141);
            var slap = Biquad.LowPass(900f);
            var lo = Biquad.BandPass(600f, 0.8f); var hi = Biquad.BandPass(2600f, 0.8f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = Mathf.Clamp01(t / 0.3f), x = White(rng);
                float swish = (lo.Run(x) * (1f - s) + hi.Run(x) * s) * Mathf.Sin(Mathf.Clamp01(t / 0.32f) * Mathf.PI) * 0.6f;
                float hitV = t > 0.32f ? slap.Run(White(rng)) * Mathf.Exp(-(t - 0.32f) * 45f) * 2.5f : 0f;
                d[i] = swish + hitV;
            }
            Normalize(d, 0.5f);
            return Clip("CordWhip", d, 1);
        }

        /// <summary>A cord being reeled in hand over hand: it hisses along the floor, with a soft slap of each loop. Loops.</summary>
        public static AudioClip CordReel()
        {
            int fade = Rate / 10, n = Rate * 2 + fade;
            var raw = new float[n];
            var rng = new System.Random(5151);
            var drag = Biquad.BandPass(1300f, 0.7f); var slap = Biquad.LowPass(700f);
            float next = 0.05f, env = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                if (t >= next) { env = 0.8f + (float)rng.NextDouble() * 0.4f; next = t + 0.3f + (float)rng.NextDouble() * 0.12f; }
                env *= 0.9992f;
                float rub = 0.55f + 0.45f * Mathf.Sin(t * 2f * Mathf.PI * 3.1f + Mathf.Sin(t * 5f));
                float x = White(rng);
                raw[i] = drag.Run(x) * 0.35f * rub + slap.Run(x) * Mathf.Pow(env, 24f) * 1.6f;
            }
            var d = LoopJoin(raw, fade);
            Normalize(d, 0.4f);
            return Clip("CordReel", d, 1);
        }

        /// <summary>
        /// The rubber chicken squeezed: a harsh rubber-reed squawk that climbs in pitch and wavers (variant changes the
        /// pitch a little), or released: the wheezy whistle of air going back in.
        /// </summary>
        public static AudioClip Chicken(bool squeeze, int variant)
        {
            float len = squeeze ? 0.8f : 0.4f;
            int n = (int)(Rate * len);
            var d = new float[n];
            var rng = new System.Random(squeeze ? 600 + variant : 700 + variant);
            float k = 1f + (variant - 1) * 0.07f;
            var f1 = Biquad.BandPass(1150f * k, 3f); var f2 = Biquad.BandPass(2700f * k, 4f); var f3 = Biquad.BandPass(4200f * k, 5f);
            var air = Biquad.BandPass(2400f * k, 1.5f);
            var wLo = Biquad.BandPass(1300f * k, 6f); var wHi = Biquad.BandPass(3100f * k, 6f);
            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                if (squeeze)
                {
                    float f = (520f + 820f * Mathf.Clamp01(t / 0.12f) + 70f * Mathf.Sin(t * 37f) + 40f * Mathf.Sin(t * 91f)) * k;
                    ph += f / Rate; ph -= Mathf.Floor(ph);
                    float saw = ph * 2f - 1f;
                    float reed = Mathf.Clamp(saw * 2.4f + White(rng) * 0.25f, -1f, 1f);
                    float env = Mathf.Min(1f, t / 0.025f) * Mathf.Clamp01((len - t) / 0.12f) * (0.85f + 0.15f * Mathf.Sin(t * 23f));
                    d[i] = (f1.Run(reed) * 1.2f + f2.Run(reed) * 1.6f + f3.Run(reed) * 0.6f + reed * 0.08f) * env + air.Run(White(rng)) * 0.05f * env;
                }
                else
                {
                    float s = t / len, x = White(rng);
                    float env = Mathf.Sin(Mathf.Clamp01(s) * Mathf.PI);
                    d[i] = ((wLo.Run(x) * (1f - s) + wHi.Run(x) * s) * 1.5f + air.Run(White(rng)) * 0.3f) * env;
                }
            }
            Normalize(d, squeeze ? 0.7f : 0.3f);
            return Clip((squeeze ? "ChickenSqueeze_" : "ChickenRelease_") + variant, d, 1);
        }

        /// <summary>
        /// The way through a seam (2026-11-09, the owner: the old crackle was "loud and annoying if you're doing it a lot"):
        /// a soft hush of tape noise, low-passed, swelling in over a tenth of a second and gone by 0.7 s, under a faint
        /// low push of air. Quiet enough to hear a hundred times.
        /// </summary>
        public static AudioClip NoclipHush()
        {
            int n = (int)(Rate * 0.75f);
            var d = new float[n];
            var rng = new System.Random(2027);
            var lp = Biquad.LowPass(2600f); var lp2 = Biquad.LowPass(2600f); var low = Biquad.LowPass(140f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = t < 0.09f ? t / 0.09f : Mathf.Exp(-(t - 0.09f) * 5.5f);
                float hiss = lp2.Run(lp.Run(White(rng))) * (0.85f + 0.15f * Mathf.Sin(t * 2f * Mathf.PI * 7f));
                float push = low.Run(White(rng)) * 3f * Mathf.Exp(-t * 9f);
                d[i] = (hiss + push) * env;
            }
            Normalize(d, 0.3f);
            return Clip("NoclipHush", d, 1);
        }

        /// <summary>The camcorder's audio as its tape glitches through a noclip door: digital crackle and dropouts.</summary>
        public static AudioClip NoclipCrackle()
        {
            int n = (int)(Rate * 0.9f);
            var d = new float[n];
            var rng = new System.Random(1313);
            var hp = Biquad.HighPass(1500f); var buzz = Biquad.BandPass(120f, 2f);
            float hold = 0f; int holdLeft = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                if (holdLeft-- <= 0) { hold = White(rng); holdLeft = rng.Next(1, 40); }    // sample-and-hold: crushed digital crackle
                bool gap = Mathf.PerlinNoise(t * 30f, 0.5f) < 0.35f;
                float env = Mathf.Min(1f, t / 0.01f) * Mathf.Clamp01((0.9f - t) / 0.25f);
                d[i] = gap ? 0f : (hp.Run(hold) * 0.8f + buzz.Run(White(rng)) * 2f + (rng.NextDouble() < 0.004 ? White(rng) * 3f : 0f)) * env;
            }
            Normalize(d, 0.5f);
            return Clip("NoclipCrackle", d, 1);
        }
    }
}
