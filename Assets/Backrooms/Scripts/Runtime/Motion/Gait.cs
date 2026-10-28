using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// Human gait numbers for the procedural body (2026-10-28), from published adult gait data (Docs/Interaction.md, "Gait"):
    /// steps a second for a speed (walking about 1.7 Hz at 0.9 m/s, 1.87 at 1.3 and 2.1 at 1.8; running about 2.75 Hz at
    /// 3.6 m/s), the share of the cycle a foot is on the ground (about 60 % walking, under 40 % running), and the swinging
    /// foot's lift and tilt (Perry's rockers: it leaves heel-high off the ball of the foot, levels, and lands on the heel,
    /// toes up).
    /// </summary>
    public static class Gait
    {
        /// <summary>How much of a run the gait is at a speed (0 walking .. 1 running), eased.</summary>
        public static float RunAmount(float speed) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.9f, 3.2f, speed));

        /// <summary>Steps a second (both feet counted) at a speed (m/s), from walking's line to running's.</summary>
        public static float Cadence(float speed, float run)
        {
            speed = Mathf.Max(0f, speed);
            float walk = 1.25f + 0.47f * speed;
            float running = 2.35f + 0.11f * speed;
            return Mathf.Lerp(walk, running, Mathf.Clamp01(run));
        }

        /// <summary>One step's length (m) at a speed: the speed over the cadence.</summary>
        public static float StepLength(float speed, float run) => Mathf.Max(0f, speed) / Cadence(speed, run);

        /// <summary>The share of a gait cycle one foot is on the ground: about 0.63 slow, 0.58 fast walking, 0.38 running.</summary>
        public static float StanceShare(float speed, float run)
            => Mathf.Lerp(Mathf.Lerp(0.63f, 0.58f, Mathf.InverseLerp(0.6f, 1.8f, speed)), 0.38f, Mathf.Clamp01(run));

        /// <summary>
        /// The swinging foot's lift over its step (t 0..1; 0 at lift-off and landing, 1 at its highest): highest a little
        /// before the middle, as the knee folds after toe-off, lower as it comes down to the heel strike.
        /// </summary>
        public static float SwingLift(float t)
        {
            t = Mathf.Clamp01(t);
            return Mathf.Sin(t * Mathf.PI) * (1f - 0.4f * t) / 0.81f;
        }

        /// <summary>
        /// The swinging foot's tilt (degrees, heel up positive) at t: from its tilt at toe-off, levelling through the first
        /// two thirds of the swing, to the landing's (toes up for a heel strike, negative).
        /// </summary>
        public static float SwingPitch(float t, float liftPitch, float landPitch)
        {
            t = Mathf.Clamp01(t);
            return liftPitch * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.7f, t)))
                   + landPitch * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, t));
        }

        /// <summary>
        /// A planted foot's heel rise (degrees) for how far it is behind the hip, as a share of where it leaves the floor
        /// (0 under the hip .. 1 at toe-off): flat through mid-stance, rising faster toward toe-off (the forefoot rocker).
        /// </summary>
        public static float HeelRise(float behindShare, float toeOffPitch)
            => toeOffPitch * Mathf.Pow(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 1f, behindShare)), 1.5f);

        /// <summary>
        /// Soft IK (after Andy Nicholas): the last 'soft' metres before a limb is straight are eased, so as a target comes into
        /// or out of reach the knee straightens and bends smoothly instead of snapping (a knee's angle changes fastest just
        /// short of straight: about 15 degrees over the last centimetre). A far target is fallen short of by at most 'soft'.
        /// Returns the root-to-end distance to solve for.
        /// </summary>
        public static float SoftReach(float distance, float length, float soft)
        {
            float start = length - soft;
            if (soft <= 1e-5f || distance <= start) return distance;
            return start + soft * (1f - Mathf.Exp(-(distance - start) / soft));
        }
    }
}
