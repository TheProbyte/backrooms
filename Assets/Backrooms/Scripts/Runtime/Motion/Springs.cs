using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// Springs for motion that should feel like a body, not a tween (after Daniel Holden's "Spring-It-On"):
    ///  - <see cref="Damper"/>: a critically damped spring set by its half-life (the time to close half the gap). It keeps
    ///    its velocity when the target jumps, so a change of pose or direction carries on smoothly instead of snapping or
    ///    fading linearly: that is inertialization (the old pose's offset decaying with its velocity).
    ///  - <see cref="Oscillator"/>: an under-damped spring (a pendulum) set by frequency and damping ratio, for things that
    ///    swing and settle: a carried load, a hanging arm, a shoved torso.
    /// Exact for any frame time (no stiffness blow-ups at low frame rates).
    /// </summary>
    public static class Springs
    {
        const float Ln2 = 0.69314718f;

        static float NegExp(float x) => 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);

        public static float Damper(float x, ref float v, float target, float halfLife, float dt)
        {
            float y = 2f * Ln2 / Mathf.Max(halfLife, 1e-4f);
            float j0 = x - target, j1 = v + j0 * y;
            float e = NegExp(y * dt);
            v = e * (v - j1 * y * dt);
            return e * (j0 + j1 * dt) + target;
        }

        public static Vector3 Damper(Vector3 x, ref Vector3 v, Vector3 target, float halfLife, float dt)
        {
            float y = 2f * Ln2 / Mathf.Max(halfLife, 1e-4f);
            Vector3 j0 = x - target, j1 = v + j0 * y;
            float e = NegExp(y * dt);
            v = e * (v - j1 * (y * dt));
            return e * (j0 + j1 * dt) + target;
        }

        /// <summary>An angle (degrees) that takes the short way round.</summary>
        public static float DamperAngle(float x, ref float v, float target, float halfLife, float dt)
            => Damper(x, ref v, x + Mathf.DeltaAngle(x, target), halfLife, dt);

        /// <summary>An under-damped spring toward target: frequency (Hz), damping ratio (1 = no overshoot, 0.3 = swings).</summary>
        public static float Oscillator(float x, ref float v, float target, float freq, float damping, float dt)
        {
            float w = 2f * Mathf.PI * freq;
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f));
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                v += (-(x - target) * w * w - v * 2f * damping * w) * h;
                x += v * h;
            }
            return x;
        }

        public static Vector3 Oscillator(Vector3 x, ref Vector3 v, Vector3 target, float freq, float damping, float dt)
        {
            float w = 2f * Mathf.PI * freq;
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f));
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                v += (-(x - target) * w * w - v * 2f * damping * w) * h;
                x += v * h;
            }
            return x;
        }
    }
}
