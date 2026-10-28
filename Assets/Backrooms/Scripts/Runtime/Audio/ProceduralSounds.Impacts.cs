using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>What a struck thing is made of, for its own ring (ImpactAudio).</summary>
    public enum ImpactBody { Can, Plastic, Chalk, Tape, LampMetal, Wood, Cardboard, Soft, Chair }

    /// <summary>What it struck (ImpactAudio).</summary>
    public enum ImpactSurface { Carpet, HardFloor, Concrete, Tile, Drywall, Metal, Soft }

    /// <summary>
    /// Impacts and scrapes of things moving in the world (2026-10-28), made by modal synthesis, as physically based game
    /// audio does: a short contact burst (harder materials: brighter and shorter) and the struck body's own resonances,
    /// damped sinusoids at that material's partials, each with its own decay. The thing that hits and the thing it hits are
    /// separate sounds, played together (ImpactAudio), so a can on carpet is a muffled tick, the same can on concrete a
    /// bright ring with a hard click, and a sofa shoved into a wall a soft thump and the drywall's hollow boom.
    /// </summary>
    public static partial class ProceduralSounds
    {
        /// <summary>Damped partials: frequencies (Hz, times scale), amplitudes and decay times (s), each slightly detuned per variant.</summary>
        static void Modes(float[] d, int at, float gain, float[] f, float[] a, float[] decay, float scale, System.Random rng)
        {
            int n = f.Length;
            var ph = new float[n]; var fr = new float[n];
            for (int k = 0; k < n; k++) { ph[k] = (float)rng.NextDouble() * TwoPi; fr[k] = f[k] * scale * (1f + ((float)rng.NextDouble() - 0.5f) * 0.06f); }
            float longest = 0f;
            foreach (var t in decay) longest = Mathf.Max(longest, t);
            int len = (int)(Rate * longest * 6f);
            for (int i = 0; i < len && at + i < d.Length; i++)
            {
                float t = i / (float)Rate, s = 0f;
                for (int k = 0; k < n; k++) s += a[k] * Mathf.Sin(TwoPi * fr[k] * t + ph[k]) * Mathf.Exp(-t / decay[k]);
                d[at + i] += s * gain;
            }
        }

        /// <summary>The strike itself: a band-passed noise burst, brighter and shorter the harder the materials.</summary>
        static void Contact(float[] d, int at, float gain, float hz, float seconds, System.Random rng)
        {
            var bp = Biquad.BandPass(hz, 0.8f);
            int len = (int)(Rate * seconds * 5f);
            for (int i = 0; i < len && at + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[at + i] += bp.Run(White(rng)) * Mathf.Exp(-t / seconds) * gain * 2.2f;
            }
        }

        /// <summary>A body struck (pitch is set at playback by its size).</summary>
        public static AudioClip ImpactBodyClip(ImpactBody body, int variant)
        {
            var d = new float[(int)(Rate * 0.9f)];
            var rng = new System.Random(1700 + (int)body * 31 + variant * 7);
            switch (body)
            {
                case ImpactBody.Can:
                    // A thin aerosol can: bright, quick inharmonic ring, and the mixing ball rattling a few times.
                    Contact(d, 0, 0.5f, 4200f, 0.0012f, rng);
                    Modes(d, 0, 0.5f, new[] { 1150f, 2480f, 3900f, 5350f, 6900f }, new[] { 1f, 0.7f, 0.5f, 0.35f, 0.2f },
                          new[] { 0.2f, 0.15f, 0.1f, 0.075f, 0.055f }, 1f, rng);
                    for (int k = 0; k < 3 + variant; k++)
                        Contact(d, (int)(Rate * (0.03f + 0.035f * k + 0.01f * (float)rng.NextDouble())), 0.18f / (k + 1), 4800f, 0.0008f, rng);
                    break;
                case ImpactBody.Plastic:
                    // A plastic housing (the cord reel, a plug): a hollow tock, little ring.
                    Contact(d, 0, 0.6f, 2600f, 0.0018f, rng);
                    Modes(d, 0, 0.55f, new[] { 190f, 430f, 990f, 1860f, 3100f }, new[] { 0.6f, 1f, 0.6f, 0.35f, 0.2f },
                          new[] { 0.03f, 0.045f, 0.03f, 0.02f, 0.012f }, 1f, rng);
                    break;
                case ImpactBody.Chalk:
                    // A stick of chalk: a tiny dry click.
                    Contact(d, 0, 0.7f, 5200f, 0.0009f, rng);
                    Modes(d, 0, 0.25f, new[] { 3300f, 6200f, 8100f }, new[] { 1f, 0.6f, 0.3f }, new[] { 0.018f, 0.01f, 0.007f }, 1f, rng);
                    break;
                case ImpactBody.Tape:
                    // A roll of tape: the cardboard core's dull knock under the plastic.
                    Contact(d, 0, 0.45f, 1900f, 0.002f, rng);
                    Thump(d, 0, 0.5f, 320f, 0.018f, rng);
                    Modes(d, 0, 0.25f, new[] { 1220f, 2650f }, new[] { 1f, 0.5f }, new[] { 0.025f, 0.015f }, 1f, rng);
                    break;
                case ImpactBody.LampMetal:
                    // A desk lamp: the weighted base's thunk and the steel shade ringing.
                    Contact(d, 0, 0.5f, 3400f, 0.0012f, rng);
                    Thump(d, 0, 0.45f, 150f, 0.02f, rng);
                    Modes(d, 0, 0.42f, new[] { 520f, 1280f, 2350f, 3600f, 5100f }, new[] { 0.8f, 1f, 0.7f, 0.45f, 0.25f },
                          new[] { 0.45f, 0.32f, 0.22f, 0.14f, 0.09f }, 1f, rng);
                    break;
                case ImpactBody.Wood:
                    // A table or chair frame: a woody knock.
                    Contact(d, 0, 0.45f, 2200f, 0.0015f, rng);
                    WoodKnock(d, 0, 0.7f, 260f + 40f * variant, rng);
                    break;
                case ImpactBody.Cardboard:
                    // A cardboard box: a hollow papery thump.
                    Thump(d, 0, 0.7f, 140f + 15f * variant, 0.035f, rng);
                    {
                        var bp = Biquad.BandPass(1500f, 0.6f);
                        int len = (int)(Rate * 0.06f);
                        for (int i = 0; i < len; i++) d[i] += bp.Run(White(rng)) * Mathf.Exp(-i / (Rate * 0.012f)) * 0.5f;
                    }
                    break;
                case ImpactBody.Soft:
                    // Upholstery: a soft heavy whump, no ring at all.
                    Thump(d, 0, 0.9f, 72f + 8f * variant, 0.05f, rng);
                    {
                        var lp = Biquad.LowPass(700f);
                        int len = (int)(Rate * 0.12f);
                        for (int i = 0; i < len; i++) d[i] += lp.Run(White(rng)) * Mathf.Exp(-i / (Rate * 0.03f)) * 0.35f;
                    }
                    break;
                case ImpactBody.Chair:
                    // An office chair: the plastic shell's tock and the steel base's tick.
                    Contact(d, 0, 0.45f, 2600f, 0.0016f, rng);
                    Modes(d, 0, 0.45f, new[] { 230f, 520f, 1150f, 2400f, 3900f }, new[] { 0.7f, 1f, 0.55f, 0.45f, 0.25f },
                          new[] { 0.04f, 0.05f, 0.035f, 0.12f, 0.08f }, 1f, rng);
                    break;
            }
            Normalize(d, 0.75f);
            return Clip("ImpactBody_" + body + "_" + variant, Trim(d), 1);
        }

        /// <summary>The surface's answer to being struck.</summary>
        public static AudioClip ImpactSurfaceClip(ImpactSurface surface, int variant)
        {
            var d = new float[(int)(Rate * 0.8f)];
            var rng = new System.Random(1900 + (int)surface * 37 + variant * 11);
            switch (surface)
            {
                case ImpactSurface.Carpet:
                    // Pile over a concrete slab: a short muffled thud, the fibres' soft hiss.
                    Thump(d, 0, 0.8f, 105f + 10f * variant, 0.016f, rng);
                    {
                        var lp = Biquad.LowPass(500f);
                        int len = (int)(Rate * 0.04f);
                        for (int i = 0; i < len; i++) d[i] += lp.Run(White(rng)) * Mathf.Exp(-i / (Rate * 0.008f)) * 0.4f;
                    }
                    break;
                case ImpactSurface.HardFloor:
                    // Wood or laminate: a click and a hollow knock.
                    Contact(d, 0, 0.6f, 2800f, 0.0012f, rng);
                    WoodKnock(d, 0, 0.6f, 210f + 25f * variant, rng);
                    break;
                case ImpactSurface.Concrete:
                    // Concrete: a hard bright click and almost no body (it does not give).
                    Contact(d, 0, 0.9f, 4600f, 0.0009f, rng);
                    Thump(d, 0, 0.35f, 95f, 0.012f, rng);
                    break;
                case ImpactSurface.Tile:
                    // Ceramic tile: a click with a short glassy ring.
                    Contact(d, 0, 0.8f, 5000f, 0.0009f, rng);
                    Modes(d, 0, 0.3f, new[] { 2150f, 3580f, 5200f }, new[] { 1f, 0.6f, 0.3f }, new[] { 0.035f, 0.025f, 0.015f }, 1f, rng);
                    break;
                case ImpactSurface.Drywall:
                    // Gypsum board on studs: a hollow boom (the cavity behind it) and a dull slap.
                    Contact(d, 0, 0.35f, 1600f, 0.002f, rng);
                    Thump(d, 0, 0.75f, 112f + 12f * variant, 0.07f, rng);
                    Modes(d, 0, 0.25f, new[] { 470f, 760f }, new[] { 1f, 0.6f }, new[] { 0.03f, 0.02f }, 1f, rng);
                    break;
                case ImpactSurface.Metal:
                    // Rack steel, a duct: a clang.
                    Contact(d, 0, 0.5f, 3600f, 0.001f, rng);
                    SheetMetal(d, 0, 0.7f, 0.85f + 0.1f * variant, 0.22f, rng);
                    break;
                case ImpactSurface.Soft:
                    Thump(d, 0, 0.6f, 80f, 0.03f, rng);
                    break;
            }
            Normalize(d, 0.75f);
            return Clip("ImpactSurface_" + surface + "_" + variant, Trim(d), 1);
        }

        /// <summary>
        /// Something sliding along the floor (looped; its level and pitch follow the slip speed). kind 0: dragged over carpet
        /// (a soft rasp of fibres), 1: scraped over a hard floor (stick-slip squeaks and grit), 2: casters rolling.
        /// </summary>
        public static AudioClip ScrapeLoop(int kind)
        {
            int fade = Rate / 8, n = Rate * 2 + fade;
            var raw = new float[n];
            var rng = new System.Random(2100 + kind);
            if (kind == 0)
            {
                var bp = Biquad.BandPass(850f, 0.6f); var lp = Biquad.LowPass(1700f);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float tuft = 0.55f + 0.45f * Mathf.PerlinNoise(t * 31f, 0.3f);
                    raw[i] = lp.Run(bp.Run(White(rng))) * tuft;
                }
            }
            else if (kind == 1)
            {
                var bp = Biquad.BandPass(2100f, 1.3f); var low = Biquad.BandPass(320f, 0.9f);
                float slip = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    // Stick-slip: the contact catches and lets go a few dozen times a second.
                    if (rng.NextDouble() < 32.0 / Rate) slip = 0.6f + (float)rng.NextDouble() * 0.6f;
                    slip *= 0.9993f;
                    float x = White(rng);
                    raw[i] = bp.Run(x) * (0.25f + slip) + low.Run(x) * 0.5f * (0.6f + 0.4f * Mathf.PerlinNoise(t * 9f, 1.7f));
                }
            }
            else
            {
                var lp = Biquad.LowPass(240f); var bp = Biquad.BandPass(900f, 1.4f);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    // A low rumble with the wheels' small regular bumps (a seam in each tyre, five wheels slightly apart).
                    float bump = 0f;
                    for (int w = 0; w < 5; w++) bump += Mathf.Pow(Mathf.Max(0f, Mathf.Sin(TwoPi * (6f + w * 0.37f) * t + w)), 24f);
                    float x = White(rng);
                    raw[i] = lp.Run(x) * 1.2f + bp.Run(x) * 0.15f * bump;
                }
            }
            var dd = LoopJoin(raw, fade);
            Normalize(dd, 0.4f);
            return Clip("Scrape_" + kind, dd, 1);
        }

        /// <summary>Drops the silent tail (the buffers are long enough for the slowest ring).</summary>
        static float[] Trim(float[] d)
        {
            int end = d.Length;
            while (end > Rate / 50 && Mathf.Abs(d[end - 1]) < 1e-4f) end--;
            if (end == d.Length) return d;
            var o = new float[end];
            System.Array.Copy(d, o, end);
            for (int i = 0; i < Mathf.Min(256, end); i++) o[end - 1 - i] *= i / 256f;     // no click at the cut
            return o;
        }
    }
}
