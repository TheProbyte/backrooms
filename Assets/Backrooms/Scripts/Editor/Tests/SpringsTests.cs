using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The springs the body and movement are built on (Motion/Springs.cs).</summary>
    public class SpringsTests
    {
        [Test]
        public void ADamperClosesHalfTheGapInItsHalfLife()
        {
            float x = 0f, v = 0f;
            const float half = 0.2f, dt = 1f / 60f;
            // From rest it starts slowly (critically damped, not exponential): by then it has closed a bit less than half.
            for (float t = 0f; t < half - 1e-4f; t += dt) x = Springs.Damper(x, ref v, 1f, half, dt);
            Assert.That(x, Is.InRange(0.35f, 0.55f));
            for (int i = 0; i < 300; i++) x = Springs.Damper(x, ref v, 1f, half, dt);
            Assert.AreEqual(1f, x, 1e-3f, "it settles on the target");
        }

        [Test]
        public void ADamperNeverOvershootsAndKeepsItsVelocityThroughAJump()
        {
            float x = 0f, v = 0f, max = 0f;
            for (int i = 0; i < 120; i++) { x = Springs.Damper(x, ref v, 1f, 0.1f, 1f / 60f); max = Mathf.Max(max, x); }
            Assert.LessOrEqual(max, 1.0001f, "critically damped: no overshoot");
            // Moving, the target jumps back: it does not reverse at once (inertialization), it carries on and turns.
            x = 0f; v = 0f;
            for (int i = 0; i < 6; i++) x = Springs.Damper(x, ref v, 1f, 0.1f, 1f / 60f);
            float before = x;
            x = Springs.Damper(x, ref v, 0f, 0.1f, 1f / 60f);
            Assert.Greater(x, before, "still going the old way for a moment");
        }

        [Test]
        public void AnOscillatorSwingsPastWhenLightlyDampedAndNotWhenCritically()
        {
            float Peak(float damping)
            {
                float x = 0f, v = 0f, max = 0f;
                for (int i = 0; i < 240; i++) { x = Springs.Oscillator(x, ref v, 1f, 2f, damping, 1f / 60f); max = Mathf.Max(max, x); }
                return max;
            }
            Assert.Greater(Peak(0.3f), 1.2f, "a pendulum swings past");
            Assert.Less(Peak(1f), 1.01f, "critically damped: it does not");
        }

        [Test]
        public void SpringsAreTheSameAtAnyFrameRate()
        {
            float Run(float dt)
            {
                float x = 0f, v = 0f;
                for (float t = 0f; t < 0.5f - 1e-5f; t += dt) x = Springs.Damper(x, ref v, 1f, 0.12f, dt);
                return x;
            }
            Assert.AreEqual(Run(1f / 144f), Run(1f / 20f), 0.03f);
        }
    }
}
