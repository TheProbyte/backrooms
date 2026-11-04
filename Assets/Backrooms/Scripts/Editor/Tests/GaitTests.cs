using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The gait numbers the body walks by (Motion/Gait.cs) against published adult gait data.</summary>
    public class GaitTests
    {
        [Test]
        public void CadenceAndStepLengthMatchAdultWalkingAndRunning()
        {
            // Walking: slow 0.93 m/s ~100 steps/min, preferred 1.32 m/s ~112 (step ~0.71 m), fast 1.78 m/s ~125.
            Assert.That(Gait.Cadence(0.93f, 0f) * 60f, Is.InRange(95f, 106f));
            Assert.That(Gait.Cadence(1.32f, 0f) * 60f, Is.InRange(106f, 118f));
            Assert.That(Gait.StepLength(1.32f, 0f), Is.InRange(0.66f, 0.76f));
            Assert.That(Gait.Cadence(1.78f, 0f) * 60f, Is.InRange(118f, 132f));
            // Running 3.6 m/s: ~160-170 steps/min, steps of ~1.3 m.
            float run = Gait.RunAmount(3.6f);
            Assert.AreEqual(1f, run, 1e-4f);
            Assert.That(Gait.Cadence(3.6f, run) * 60f, Is.InRange(158f, 172f));
            Assert.That(Gait.StepLength(3.6f, run), Is.InRange(1.2f, 1.4f));
            // Faster is never fewer steps a second, from a standstill to a run.
            float last = 0f;
            for (float v = 0f; v <= 4f; v += 0.05f)
            {
                float c = Gait.Cadence(v, Gait.RunAmount(v));
                Assert.GreaterOrEqual(c, last - 1e-4f, $"cadence falls at {v} m/s");
                last = c;
            }
        }

        [Test]
        public void StanceShareIsAWalksSixtyPercentAndARunsUnderForty()
        {
            Assert.That(Gait.StanceShare(1.32f, 0f), Is.InRange(0.58f, 0.63f));
            Assert.That(Gait.StanceShare(3.6f, Gait.RunAmount(3.6f)), Is.InRange(0.33f, 0.42f));
        }

        [Test]
        public void TheSwingingFootLeavesAndLandsOnTheFloorAndRisesEarly()
        {
            Assert.AreEqual(0f, Gait.SwingLift(0f), 1e-4f);
            Assert.AreEqual(0f, Gait.SwingLift(1f), 1e-4f);
            float peak = 0f, at = 0f;
            for (float t = 0f; t <= 1f; t += 0.01f) { float y = Gait.SwingLift(t); if (y > peak) { peak = y; at = t; } }
            Assert.AreEqual(1f, peak, 0.01f, "normalised to its highest");
            Assert.Less(at, 0.5f, "highest before the middle of the swing");
            // Tilt: continuous with toe-off at the start, the landing's at the end, level in between.
            Assert.AreEqual(40f, Gait.SwingPitch(0f, 40f, -15f), 1e-3f);
            Assert.AreEqual(-15f, Gait.SwingPitch(1f, 40f, -15f), 1e-3f);
            Assert.That(Gait.SwingPitch(0.6f, 40f, -15f), Is.InRange(-6f, 8f));
        }

        [Test]
        public void SoftReachEasesTheLastStretchToStraightWithoutAJump()
        {
            const float L = 0.875f, soft = 0.026f;
            Assert.AreEqual(0.6f, Gait.SoftReach(0.6f, L, soft), 1e-6f, "well within reach: unchanged");
            Assert.AreEqual(L - soft, Gait.SoftReach(L - soft, L, soft), 1e-6f, "continuous where the easing starts");
            float last = 0f, lastSlope = 1f;
            for (float d = 0.7f; d < 1.2f; d += 0.001f)
            {
                float r = Gait.SoftReach(d, L, soft);
                Assert.LessOrEqual(r, L, "never past straight");
                Assert.GreaterOrEqual(r, last - 1e-6f, "monotonic");
                if (last > 0f)
                {
                    float slope = (r - last) / 0.001f;
                    Assert.LessOrEqual(slope, lastSlope + 1e-3f, "the slope only eases off (no kink back up)");
                    lastSlope = slope;
                }
                last = r;
            }
            Assert.AreEqual(L, Gait.SoftReach(5f, L, soft), 1e-3f, "far out of reach: as straight as it goes");
            Assert.AreEqual(0.95f, Gait.SoftReach(0.95f, L, 0f), 1e-6f, "no softness: unchanged");
        }

        [Test]
        public void TheHeelStaysDownThroughMidStanceAndRisesToToeOff()
        {
            Assert.AreEqual(0f, Gait.HeelRise(0f, 40f), 1e-4f);
            Assert.AreEqual(0f, Gait.HeelRise(0.15f, 40f), 1e-4f);
            Assert.AreEqual(40f, Gait.HeelRise(1f, 40f), 1e-3f);
            float last = 0f;
            for (float s = 0f; s <= 1.2f; s += 0.02f) { float p = Gait.HeelRise(s, 40f); Assert.GreaterOrEqual(p, last - 1e-4f); last = p; }
            Assert.Less(Gait.HeelRise(0.5f, 40f), 15f, "most of the rise comes late");
        }
    }
}
