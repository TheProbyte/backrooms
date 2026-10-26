using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>Sounds of the wooden doors: the lever and latch, the door shutting, the hinges creaking, a bump at the stop.</summary>
    public static partial class ProceduralSounds
    {
        /// <summary>A short ringing tick of metal (a click of the latch or lever).</summary>
        static void MetalTick(float[] d, int at, float gain, float hz, System.Random rng)
        {
            var bp = Biquad.BandPass(hz, 3f); var bp2 = Biquad.BandPass(hz * 2.3f, 4f);
            int len = Rate / 12;
            for (int i = 0; i < len && at + i < d.Length; i++)
            {
                float e = Mathf.Exp(-i / (Rate * 0.006f));
                float x = White(rng) * e;
                d[at + i] += (bp.Run(x) * 1.6f + bp2.Run(x)) * gain;
            }
        }

        /// <summary>A dull knock of wood (the door against its stop or the frame).</summary>
        static void WoodKnock(float[] d, int at, float gain, float hz, System.Random rng)
        {
            var bp = Biquad.BandPass(hz, 1.2f); var body = Biquad.LowPass(hz * 0.6f);
            int len = Rate / 4;
            for (int i = 0; i < len && at + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float e = Mathf.Exp(-t / 0.045f);
                float x = White(rng) * e;
                d[at + i] += (bp.Run(x) * 1.2f + body.Run(x) * 2f + Mathf.Sin(t * 2f * Mathf.PI * hz * 0.45f) * e * 0.5f) * gain;
            }
        }

        /// <summary>The lever pushed down and the latch drawing back: two clicks and a small clunk.</summary>
        public static AudioClip DoorLatch()
        {
            var d = new float[Rate / 3];
            var rng = new System.Random(421);
            MetalTick(d, 0, 0.6f, 3100f, rng);
            MetalTick(d, Rate / 40, 0.9f, 1900f, rng);
            WoodKnock(d, Rate / 40, 0.15f, 600f, rng);
            Normalize(d, 0.5f);
            return Clip("DoorLatch", d, 1);
        }

        /// <summary>The door shutting: a deep wooden thud with the latch snapping in.</summary>
        public static AudioClip DoorShut()
        {
            var d = new float[Rate / 2];
            var rng = new System.Random(422);
            WoodKnock(d, 0, 1f, 160f, rng);
            WoodKnock(d, Rate / 200, 0.4f, 420f, rng);
            MetalTick(d, Rate / 120, 0.5f, 2600f, rng);
            Normalize(d, 0.8f);
            return Clip("DoorShut", d, 1);
        }

        /// <summary>The door against its stop when swung wide: a soft knock.</summary>
        public static AudioClip DoorBump()
        {
            var d = new float[Rate / 3];
            var rng = new System.Random(423);
            WoodKnock(d, 0, 1f, 240f, rng);
            Normalize(d, 0.5f);
            return Clip("DoorBump", d, 1);
        }

        /// <summary>Old hinges turning: a slow stick-slip squeak with a wandering pitch. Loops.</summary>
        public static AudioClip DoorCreak()
        {
            int fade = Rate / 8, n = Rate * 3 + fade;
            var raw = new float[n];
            var rng = new System.Random(424);
            var bp = Biquad.BandPass(1100f, 6f); var bp2 = Biquad.BandPass(2300f, 5f); var low = Biquad.BandPass(380f, 2f);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float rate = 55f + 35f * Mathf.Sin(t * 1.7f) + 20f * Mathf.PerlinNoise(t * 3f, 0.3f);
                phase += rate / Rate;
                float pulse = 0f;
                if (phase >= 1f) { phase -= 1f; pulse = 0.6f + (float)rng.NextDouble() * 0.6f; }
                float amp = 0.4f + 0.6f * Mathf.PerlinNoise(t * 0.9f, 4.1f);
                raw[i] = (bp.Run(pulse) * 1.4f + bp2.Run(pulse) * 0.7f + low.Run(pulse) * 0.5f + White(rng) * 0.01f) * amp;
            }
            var d = LoopJoin(raw, fade);
            Normalize(d, 0.45f);
            return Clip("DoorCreak", d, 1);
        }
    }

    public static partial class SoundBank
    {
        static AudioClip s_doorLatch, s_doorShut, s_doorBump, s_doorCreak;
        public static AudioClip DoorLatch => s_doorLatch ? s_doorLatch : (s_doorLatch = ProceduralSounds.DoorLatch());
        public static AudioClip DoorShut => s_doorShut ? s_doorShut : (s_doorShut = ProceduralSounds.DoorShut());
        public static AudioClip DoorBump => s_doorBump ? s_doorBump : (s_doorBump = ProceduralSounds.DoorBump());
        public static AudioClip DoorCreak => s_doorCreak ? s_doorCreak : (s_doorCreak = ProceduralSounds.DoorCreak());
    }
}
