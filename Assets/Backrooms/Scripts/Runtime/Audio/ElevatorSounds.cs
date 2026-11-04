using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The elevator's sounds, made in code (2026-11-04, the owner's "sound walls block" and the status notes' "elevator:
    /// sound (hum, ding, door)"): a traction machine's hum (its mains-frequency drone and harmonics, the motor's whine, the
    /// rails' rumble under it), the cab's ventilation fan, the doors' operator (a small motor's whir and the rollers in their
    /// track) and their thunk at the end, and the hall lantern's chime: an electronic bell under 1.5 kHz, as elevator codes
    /// want arrival signals (ASME A17.1, ADA: once for up, twice for down).
    /// </summary>
    public static class ElevatorSounds
    {
        public static AudioClip Motor, Fan, DoorRun, DoorShut, Ding;
        const int Rate = 44100;

        static AudioClip Clip(string name, float[] d) { var c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0); return c; }

        public static void Make()
        {
            if (Motor) return;
            var r = new System.Random(7719);
            float N() => (float)(r.NextDouble() * 2.0 - 1.0);
            const float TwoPi = Mathf.PI * 2f;
            // The machine: 120 Hz drone with its harmonics, a whine near 900 Hz, the rails' low rumble (loops: 4 s).
            {
                int n = Rate * 4; var d = new float[n]; float lp = 0f, lp2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.02f; lp2 += (lp - lp2) * 0.03f;
                    float drone = Mathf.Sin(TwoPi * 120f * t) * 0.35f + Mathf.Sin(TwoPi * 240f * t) * 0.18f + Mathf.Sin(TwoPi * 360f * t) * 0.07f;
                    float whine = Mathf.Sin(TwoPi * 900f * t + 0.6f * Mathf.Sin(TwoPi * 3.5f * t)) * 0.05f;
                    d[i] = (drone + whine) * 0.6f + lp2 * 7f;
                }
                Loop(d);
                Motor = Clip("LiftMotor", d);
            }
            // The cab's fan: soft broadband air, a little blade tone.
            {
                int n = Rate * 3; var d = new float[n]; float lp = 0f, lp2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.12f; lp2 += (lp - lp2) * 0.25f;
                    d[i] = lp2 * 1.6f + Mathf.Sin(TwoPi * 186f * t) * 0.02f;
                }
                Loop(d);
                Fan = Clip("LiftFan", d);
            }
            // The doors running: the operator's whir and the rollers rumbling in their track.
            {
                int n = Rate * 2; var d = new float[n]; float lp = 0f, bp = 0f, bp2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.04f;
                    bp += (N() - bp) * 0.3f; bp2 += (bp - bp2) * 0.3f;
                    float whir = Mathf.Sin(TwoPi * 310f * t) * 0.12f + Mathf.Sin(TwoPi * 620f * t) * 0.05f;
                    d[i] = whir + lp * 3f + (bp - bp2) * 0.15f * (0.6f + 0.4f * Mathf.Sin(TwoPi * 13f * t));
                }
                Loop(d);
                DoorRun = Clip("LiftDoorRun", d);
            }
            // The doors meeting: a low thunk and the latch's click.
            {
                int n = (int)(0.35f * Rate); var d = new float[n]; float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.15f;
                    d[i] = Mathf.Sin(TwoPi * (85f - 40f * t) * t) * Mathf.Exp(-t * 18f) * 0.8f + lp * Mathf.Exp(-t * 60f) * 1.5f
                         + N() * Mathf.Exp(-(t - 0.05f) * (t - 0.05f) * 40000f) * 0.25f;
                }
                DoorShut = Clip("LiftDoorShut", d);
            }
            // The chime: a struck bell at 1047 Hz (C6) with its partials, a soft onset, ringing down over a second and a half.
            {
                int n = (int)(1.8f * Rate); var d = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float on = Mathf.Clamp01(t / 0.006f);
                    d[i] = on * (Mathf.Sin(TwoPi * 1047f * t) * Mathf.Exp(-t * 2.2f) * 0.6f
                               + Mathf.Sin(TwoPi * 2094f * t) * Mathf.Exp(-t * 4.5f) * 0.18f
                               + Mathf.Sin(TwoPi * 2885f * t) * Mathf.Exp(-t * 8f) * 0.08f
                               + Mathf.Sin(TwoPi * 523.5f * t) * Mathf.Exp(-t * 1.8f) * 0.12f);
                }
                Ding = Clip("LiftChime", d);
            }
        }

        /// <summary>The end crossfaded into the start so it loops with no click.</summary>
        static void Loop(float[] d)
        {
            int f = Rate / 10;
            var head = new float[f];
            System.Array.Copy(d, head, f);
            for (int i = 0; i < f; i++)
            {
                float t = i / (float)f;
                d[d.Length - f + i] = d[d.Length - f + i] * (1f - t) + head[i] * t;
            }
        }
    }
}
